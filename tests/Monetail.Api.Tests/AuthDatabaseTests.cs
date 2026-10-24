using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Authentication;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>認証DBの追加移行と、既存データ・人物対応の保持を検証する。</summary>
public class AuthDatabaseTests
{
    [Fact]
    public async Task MigrationPreservesEveryLegacyRowTypeSchemaAndPragma()
    {
        using var fixture = new AuthDatabaseFixture();
        var legacyTables = fixture.Tables();
        var before = fixture.Snapshot(legacyTables);
        Assert.Equal(1L, fixture.Scalar("SELECT COUNT(*) FROM pragma_foreign_key_check"));

        await fixture.MigrateAsync();

        Assert.Equal(before, fixture.Snapshot(legacyTables));
        Assert.Equal(0L, fixture.Scalar("SELECT COUNT(*) FROM legacy_write_audit"));
        Assert.True(legacyTables.All(fixture.Tables().Contains));
        Assert.Contains("AspNetUserPasskeys", fixture.Tables());
        Assert.Contains("auth_user_links", fixture.Tables());
        Assert.Equal("null", fixture.Scalar("SELECT typeof(total) FROM t_receipts WHERE seq=1"));
        Assert.Equal(777L, fixture.Scalar("SELECT total FROM t_receipts WHERE seq=2"));
        Assert.Equal("real", fixture.Scalar("SELECT typeof(discount_rate) FROM t_receipt_details WHERE receipt_seq=1"));
        Assert.Equal(1L, fixture.Scalar("SELECT COUNT(*) FROM pragma_foreign_key_check"));
    }

    [Fact]
    public async Task RepeatedMigrationKeepsStandardIdentityRecordsPasskeysAndPersonLink()
    {
        using var fixture = new AuthDatabaseFixture();
        await fixture.MigrateAsync();
        await using (var services = fixture.CreateServices())
        await using (var scope = services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AuthUser>>();
            var user = new AuthUser { Id = "stable-account", UserName = "stable-account" };
            Assert.True((await users.CreateAsync(user)).Succeeded);
            Assert.False(user.IsEnabled);
            Assert.Null(user.PasswordHash);
            Assert.Null(user.Email);

            // 暗号検証ではなく、標準Identityストアの保存・再読込を確認する。
            var credential = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
            var passkey = new UserPasskeyInfo(credential, [1, 2, 3],
                new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), 7,
                ["internal"], true, true, false, [4], [5]) { Name = "synthetic-key" };
            Assert.True((await users.AddOrUpdatePasskeyAsync(user, passkey)).Succeeded);
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            Assert.True((await roles.CreateAsync(new IdentityRole("Admin"))).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, "Admin")).Succeeded);

            var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            context.UserLinks.Add(new AuthUserLink { AuthUserId = user.Id, UserCode = 3 });
            await context.SaveChangesAsync();
            await context.Database.OpenConnectionAsync();
            using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "PRAGMA foreign_keys";
            Assert.Equal(1L, await command.ExecuteScalarAsync());
        }
        var identityTables = fixture.Tables().Where(name => name.StartsWith("AspNet", StringComparison.Ordinal)
            || name == "auth_user_links").ToArray();
        var before = fixture.Snapshot(identityTables);

        await fixture.MigrateAsync();

        Assert.Equal(before, fixture.Snapshot(identityTables));
        await using var reopenedServices = fixture.CreateServices();
        await using var reopenedScope = reopenedServices.CreateAsyncScope();
        var reopenedUsers = reopenedScope.ServiceProvider.GetRequiredService<UserManager<AuthUser>>();
        var reopenedUser = Assert.IsType<AuthUser>(await reopenedUsers.FindByIdAsync("stable-account"));
        var storedKey = Assert.Single(await reopenedUsers.GetPasskeysAsync(reopenedUser));
        Assert.Equal("synthetic-key", storedKey.Name);
        Assert.Equal(7U, storedKey.SignCount);
        Assert.True(await reopenedUsers.IsInRoleAsync(reopenedUser, "Admin"));
        Assert.Equal(3L, fixture.Scalar("SELECT user_code FROM auth_user_links WHERE auth_user_id='stable-account'"));
    }

    [Fact]
    public async Task PersonLinkEnforcesNotNullUniquenessForeignKeysAndRestrictedDeletes()
    {
        using var fixture = new AuthDatabaseFixture();
        await fixture.MigrateAsync();
        await using var services = fixture.CreateServices();
        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AuthUser>>();
        foreach (var id in new[] { "account-a", "account-b", "account-c" })
            Assert.True((await users.CreateAsync(new AuthUser { Id = id, UserName = id })).Succeeded);

        using var connection = fixture.OpenForeignKeyConnection();
        AuthDatabaseFixture.Execute(connection, "INSERT INTO auth_user_links(auth_user_id,user_code) VALUES('account-a',3)");
        foreach (var sql in new[]
        {
            "INSERT INTO auth_user_links(auth_user_id,user_code) VALUES(NULL,4)",
            "INSERT INTO auth_user_links(auth_user_id,user_code) VALUES('account-b',NULL)",
            "INSERT INTO auth_user_links(auth_user_id,user_code) VALUES('account-a',4)",
            "INSERT INTO auth_user_links(auth_user_id,user_code) VALUES('account-b',3)",
            "INSERT INTO auth_user_links(auth_user_id,user_code) VALUES('missing-account',4)",
            "INSERT INTO auth_user_links(auth_user_id,user_code) VALUES('account-c',999)",
            "DELETE FROM AspNetUsers WHERE Id='account-a'",
            "DELETE FROM m_user WHERE code=3"
        })
        {
            var error = Assert.Throws<SqliteException>(() => AuthDatabaseFixture.Execute(connection, sql));
            Assert.Equal(19, error.SqliteErrorCode);
        }
        Assert.Equal(1L, fixture.Scalar("SELECT COUNT(*) FROM auth_user_links"));
        Assert.Equal(1L, fixture.Scalar("SELECT COUNT(*) FROM m_user WHERE code=3"));
        Assert.Equal(0L, fixture.Scalar("SELECT IsEnabled FROM AspNetUsers WHERE Id='account-c'"));
    }

    [Fact]
    public async Task MissingDatabaseIsRejectedWithoutCreatingAFile()
    {
        using var fixture = new AuthDatabaseFixture(createFile: false);
        await Assert.ThrowsAsync<SqliteException>(() => fixture.MigrateAsync());
        Assert.False(File.Exists(fixture.DatabasePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyOrUnrelatedExistingDatabaseIsRejectedWithoutChanges(bool unrelated)
    {
        using var fixture = new AuthDatabaseFixture(initializeLegacy: false);
        if (unrelated)
            fixture.Execute("CREATE TABLE m_user(code INTEGER PRIMARY KEY); INSERT INTO m_user VALUES(91)");
        var tables = fixture.Tables();
        var before = fixture.Snapshot(tables);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.MigrateAsync());

        Assert.Equal(tables, fixture.Tables());
        Assert.Equal(before, fixture.Snapshot(tables));
    }

    [Fact]
    public async Task ConflictingTableRollsBackPartiallyCreatedAuthenticationSchema()
    {
        using var fixture = new AuthDatabaseFixture();
        fixture.Execute("CREATE TABLE AspNetUsers(sentinel TEXT NOT NULL); INSERT INTO AspNetUsers VALUES('preserve-me')");
        var existingTables = fixture.Tables();
        var before = fixture.Snapshot(existingTables);

        await Assert.ThrowsAsync<SqliteException>(() => fixture.MigrateAsync());

        Assert.Equal(before, fixture.Snapshot(existingTables));
        Assert.Equal(existingTables, fixture.Tables().Where(name => !name.StartsWith("__EF", StringComparison.Ordinal)).ToArray());
        Assert.DoesNotContain("AspNetRoles", fixture.Tables());
        fixture.Execute("DROP TABLE AspNetUsers");
        await fixture.MigrateAsync();
        Assert.Contains("AspNetUserPasskeys", fixture.Tables());
    }

    [Fact]
    public async Task NormalApiStartupDoesNotApplyMigrationEvenWithDatabaseConfigured()
    {
        using var fixture = new AuthDatabaseFixture();
        var tables = fixture.Tables();
        var before = fixture.Snapshot(tables);
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Production").UseSetting("ConnectionStrings:Monetail", fixture.ConnectionString));
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetService<AuthDbContext>());
        Assert.Equal(tables, fixture.Tables());
        Assert.Equal(before, fixture.Snapshot(tables));
    }
}
