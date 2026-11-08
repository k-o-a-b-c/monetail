using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
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
        await db.InitializeAsync();
        await using var services = db.CreateServices();
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        Assert.Equal(context.Model.GetEntityTypes().Select(t => t.GetTableName()!).Distinct().Order(), db.Tables().Order());
        Assert.DoesNotContain(db.Tables(), t => t.StartsWith("__", StringComparison.Ordinal));
        Assert.Contains("AspNetUserPasskeys", db.Tables());
        Assert.Equal(4L, await context.Set<Monetail.Api.Features.Monetail.Taxes.TaxCategory>().LongCountAsync());
        Assert.Equal(1L, Convert.ToInt64(await context.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM OpenIddictApplications WHERE ClientId='monetail-windows'").SingleAsync()));
        Assert.DoesNotContain(context.Model.GetEntityTypes(), type => type.GetTableName() is "m_user" or "auth_user_links");
        Assert.DoesNotContain(context.Model.FindEntityType(typeof(AccountCreationRequest))!.GetProperties(), p => p.Name == "UserCode");
        var provision = scope.ServiceProvider.GetRequiredService<AdminProvisioningService>();
        var code = await provision.InitializeAdminAsync();
        Assert.NotNull(code);
        await db.InitializeAsync();
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
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.InitializeAsync());
        Assert.Equal(tables, db.Tables());
        Assert.Equal(before, db.Snapshot(tables));
    }

    [Theory]
    [InlineData("future")]
    [InlineData("missing-seed")]
    [InlineData("missing-column")]
    [InlineData("missing-index")]
    [InlineData("legacy-lock")]
    [InlineData("missing-trigger")]
    [InlineData("extra-column")]
    public async Task LegacyManagementTablesAndDamagedSchemaAreRejectedWithoutRepair(string state)
    {
        using var db = new AuthDatabaseFixture();
        await db.InitializeAsync();
        db.Execute(state switch
        {
            "future" => "CREATE TABLE __MonetailMigrationsHistory(MigrationId TEXT, ProductVersion TEXT); INSERT INTO __MonetailMigrationsHistory VALUES('old','10')",
            "missing-seed" => "DELETE FROM tax_categories WHERE code='Standard'",
            "missing-column" => "ALTER TABLE AspNetUsers DROP COLUMN DisplayName",
            "missing-index" => "DROP INDEX UserNameIndex",
            "missing-trigger" => "DROP TRIGGER trg_tax_rate_history_overlap_insert",
            "extra-column" => "ALTER TABLE ledger_categories ADD COLUMN is_enabled INTEGER",
            _ => "CREATE TABLE __EFMigrationsLock(Id INTEGER, Timestamp TEXT)"
        });
        var tables = db.Tables();
        var before = db.Snapshot(tables);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.InitializeAsync());
        Assert.Equal(before, db.Snapshot(tables));
    }

    [Fact]
    public async Task NewSchemaEnforcesAccountReferencesAndRequestUniqueness()
    {
        using var db = new AuthDatabaseFixture();
        await db.InitializeAsync();
        await using var services = db.CreateServices();
        var issued = (await services.GetRequiredService<AdminProvisioningService>().InitializeAdminAsync())!;
        using var connection = db.OpenForeignKeyConnection();
        var insert = $"INSERT INTO auth_account_creation_requests(RequestId,CreatedByAuthUserId,AuthUserId,CreatedAtUtc) VALUES('request','{issued.AuthUserId}','{issued.AuthUserId}','2026-10-27')";
        foreach (var sql in new[]
        {
            insert.Replace("'request'", "NULL"),
            insert.Replace(issued.AuthUserId, "missing"),
            "INSERT INTO auth_sessions(IdHash,AuthUserId,CreatedAtUtc,ExpiresAtUtc,VerifiedAtUtc,RevokedAtUtc) VALUES(NULL,'missing','2026-10-27','2026-11-01','2026-10-27',NULL)",
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
        await app.Database.InitializeAsync();
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        using var me = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task ConcurrentInitializationCreatesOnlyOneAdministrator()
    {
        using var db = new AuthDatabaseFixture(createFile: false);
        await Task.WhenAll(Task.Run(db.InitializeAsync), Task.Run(db.InitializeAsync));
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

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task InitializationFailureRollsBackTablesSeedsAndTriggersAndCanRestart(bool corrupt)
    {
        using var db = new AuthDatabaseFixture();
        var before = db.Snapshot(db.Tables());
        var options = new DbContextOptionsBuilder<ApiDbContext>(); ApiDbContext.Configure(options, db.ConnectionString);
        options.AddInterceptors(new FailInitialization(corrupt));
        await using (var context = new ApiDbContext(options.Options))
            await Assert.ThrowsAsync<InvalidOperationException>(() => new DatabaseInitializer(context).InitializeAsync());
        Assert.Empty(db.Tables()); Assert.Equal(before,db.Snapshot(db.Tables()));
        await db.InitializeAsync(); Assert.Contains("AspNetUserPasskeys",db.Tables());
    }

    [Fact]
    public async Task NonSqliteFileIsRejectedByteForByteWithoutReplacement()
    {
        using var db = new AuthDatabaseFixture(createFile:false);
        byte[] original = [0,255,1,128,4,5]; await File.WriteAllBytesAsync(db.DatabasePath, original);
        await Assert.ThrowsAsync<SqliteException>(() => db.InitializeAsync());
        Assert.Equal(original,await File.ReadAllBytesAsync(db.DatabasePath));
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

    /// <summary>モデルDDL/初期値作成後、トリガー作成・最終照合で失敗させる。</summary>
    private sealed class FailInitialization(bool corrupt) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(System.Data.Common.DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!corrupt && command.CommandText.Contains("CREATE TRIGGER trg_ledger_payment_methods_owner_immutable",StringComparison.Ordinal))
                throw new InvalidOperationException("synthetic initialization failure");
            return ValueTask.FromResult(result);
        }
        public override async ValueTask<int> NonQueryExecutedAsync(System.Data.Common.DbCommand command,
            CommandExecutedEventData eventData,int result,CancellationToken cancellationToken=default)
        {
            if (corrupt && command.CommandText.Contains("CREATE TRIGGER trg_ledger_payment_methods_owner_immutable",StringComparison.Ordinal))
            {
                using var mutation=command.Connection!.CreateCommand(); mutation.Transaction=command.Transaction;
                mutation.CommandText="DROP INDEX UserNameIndex"; await mutation.ExecuteNonQueryAsync(cancellationToken);
            }
            return result;
        }
    }
}
