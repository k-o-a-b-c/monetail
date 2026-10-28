using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Authentication;
using Monetail.Api.Persistence;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>新規DBの初期化・再実行・失敗時の再開と旧DBの不変を検証する。</summary>
public class AuthDatabaseTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyOrMissingFileIsPreparedWithoutPeopleAndCanBeReopened(bool missing)
    {
        using var db = new AuthDatabaseFixture(createFile: !missing);
        await db.MigrateAsync();
        await using var services = db.CreateServices();
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.DoesNotContain(context.Model.GetEntityTypes(), type => type.GetTableName() is "m_user" or "auth_user_links");
        Assert.DoesNotContain(context.Model.FindEntityType(typeof(AccountCreationRequest))!.GetProperties(), p => p.Name == "UserCode");
        var provision = scope.ServiceProvider.GetRequiredService<AdminProvisioningService>();
        var code = await provision.InitializeAdminAsync();
        Assert.NotNull(code);
        await db.MigrateAsync();
        Assert.Null(await provision.InitializeAdminAsync());
    }

    [Theory]
    [InlineData("windows")]
    [InlineData("other")]
    [InlineData("conflict")]
    public async Task UnrelatedAndWindowsDatabasesAreRejectedWithoutChanges(string state)
    {
        using var db = new AuthDatabaseFixture(initializeWindowsDatabase: state == "windows");
        if (state == "other") db.Execute("CREATE TABLE other(value BLOB); INSERT INTO other VALUES(X'00FF')");
        if (state == "conflict") db.Execute("CREATE TABLE AspNetUsers(sentinel TEXT); INSERT INTO AspNetUsers VALUES('keep')");
        var tables = db.Tables();
        var before = db.Snapshot(tables);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.MigrateAsync());
        Assert.Equal(tables, db.Tables());
        Assert.Equal(before, db.Snapshot(tables));
    }

    [Theory]
    [InlineData("future")]
    [InlineData("missing-history")]
    [InlineData("missing-column")]
    [InlineData("missing-index")]
    [InlineData("locked")]
    public async Task UnknownHistoryAndDamagedSchemaAreRejectedWithoutRepair(string state)
    {
        using var db = new AuthDatabaseFixture();
        await db.MigrateAsync();
        db.Execute(state switch
        {
            "future" => "INSERT INTO __MonetailMigrationsHistory VALUES('99999999999999_Future','99.0.0')",
            "missing-history" => "DELETE FROM __MonetailMigrationsHistory",
            "missing-column" => "ALTER TABLE AspNetUsers DROP COLUMN DisplayName",
            "missing-index" => "DROP INDEX UserNameIndex",
            _ => "INSERT INTO __EFMigrationsLock VALUES(1,'2026-10-27')"
        });
        var tables = db.Tables();
        var before = db.Snapshot(tables);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.MigrateAsync());
        Assert.Equal(before, db.Snapshot(tables));
    }

    [Fact]
    public async Task NewSchemaEnforcesAccountReferencesAndRequestUniqueness()
    {
        using var db = new AuthDatabaseFixture();
        await db.MigrateAsync();
        await using var services = db.CreateServices();
        var issued = (await services.GetRequiredService<AdminProvisioningService>().InitializeAdminAsync())!;
        using var connection = db.OpenForeignKeyConnection();
        var insert = $"INSERT INTO auth_account_creation_requests VALUES('request','{issued.AuthUserId}','{issued.AuthUserId}','2026-10-27')";
        foreach (var sql in new[]
        {
            insert.Replace("'request'", "NULL"),
            insert.Replace(issued.AuthUserId, "missing"),
            "INSERT INTO auth_sessions VALUES(NULL,'missing','2026-10-27','2026-11-01','2026-10-27',NULL)",
            $"DELETE FROM AspNetUsers WHERE Id='{issued.AuthUserId}'"
        }) Assert.Equal(19, Assert.Throws<SqliteException>(() => AuthDatabaseFixture.Execute(connection, sql)).SqliteErrorCode);
        AuthDatabaseFixture.Execute(connection, insert);
        Assert.Equal(19, Assert.Throws<SqliteException>(() => AuthDatabaseFixture.Execute(connection, insert)).SqliteErrorCode);
        Assert.Equal(19, Assert.Throws<SqliteException>(() => AuthDatabaseFixture.Execute(connection, insert.Replace("'request'", "'another'"))).SqliteErrorCode);
    }

    [Fact]
    public async Task ReexecutionPreservesRegisteredAuthenticationDataAndLogin()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        await app.LoginAsync(client);
        var before = app.Database.Snapshot(app.Database.Tables());
        await app.Database.MigrateAsync();
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        using var me = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task ConcurrentInitializationCreatesOnlyOneAdministrator()
    {
        using var db = new AuthDatabaseFixture(createFile: false);
        await Task.WhenAll(Task.Run(db.MigrateAsync), Task.Run(db.MigrateAsync));
        await using var first = db.CreateServices();
        await using var second = db.CreateServices();
        var results = await Task.WhenAll(
            Task.Run(() => first.GetRequiredService<AdminProvisioningService>().InitializeAdminAsync()),
            Task.Run(() => second.GetRequiredService<AdminProvisioningService>().InitializeAdminAsync()));
        Assert.Single(results, code => code is not null);
    }

    [Fact]
    public async Task HeldInitializationLockHonorsCancellationWithoutCreatingDatabase()
    {
        using var db = new AuthDatabaseFixture(createFile: false);
        using var held = new FileStream(db.DatabasePath + ".initialize.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await using var services = db.CreateServices();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            services.GetRequiredService<DatabaseInitializer>().InitializeAsync(timeout.Token));
        Assert.False(File.Exists(db.DatabasePath));
    }

    [Fact]
    public async Task MissingParentIsNotCreatedByInitialization()
    {
        using var db = new AuthDatabaseFixture(createFile: false);
        var parent = Path.Combine(Path.GetDirectoryName(db.DatabasePath)!, "missing");
        var options = new DbContextOptionsBuilder<ApiDbContext>();
        ApiDbContext.Configure(options, $"Data Source={Path.Combine(parent, "api.db")}");
        await using var context = new ApiDbContext(options.Options);
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => new DatabaseInitializer(context).InitializeAsync());
        Assert.False(Directory.Exists(parent));
    }

    [Fact]
    public async Task InitialMigrationFailureRollsBackAndNextInvocationCanResume()
    {
        using var db = new AuthDatabaseFixture();
        var options = new DbContextOptionsBuilder<ApiDbContext>();
        ApiDbContext.Configure(options, db.ConnectionString);
        options.AddInterceptors(new FailMigration());
        await using (var context = new ApiDbContext(options.Options))
            await Assert.ThrowsAsync<InvalidOperationException>(() => new DatabaseInitializer(context).InitializeAsync());
        Assert.DoesNotContain("AspNetUsers", db.Tables());
        Assert.DoesNotContain("AspNetRoles", db.Tables());
        await db.MigrateAsync();
        Assert.Contains("AspNetUsers", db.Tables());
    }

    [Fact]
    public async Task HistoryTablesLeftBeforeFirstMigrationAllowRestart()
    {
        using var db = new AuthDatabaseFixture();
        await using (var services = db.CreateServices())
        await using (var scope = services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ApiDbContext>().GetService<IMigrator>().MigrateAsync("0");
        await db.MigrateAsync();
        Assert.Contains("auth_sessions", db.Tables());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NormalStartupAndHealthNeverCreateOrMigrateDatabase(bool missing)
    {
        using var db = new AuthDatabaseFixture(createFile: !missing);
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseApiTestSettings(db));
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(!missing, File.Exists(db.DatabasePath));
        if (!missing) Assert.Empty(db.Tables());
    }

    /// <summary>DDLの途中で保存を失敗させ、初期移行の原子性を確認する。</summary>
    private sealed class FailMigration : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(System.Data.Common.DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("CREATE TABLE \"AspNetUserClaims\""))
                throw new InvalidOperationException("synthetic migration failure");
            return ValueTask.FromResult(result);
        }
    }
}
