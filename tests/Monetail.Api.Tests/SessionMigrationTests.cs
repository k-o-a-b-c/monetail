using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Authentication;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>登録済みの旧認証DBへ、セッション表だけを追加する移行を検証する。</summary>
public class SessionMigrationTests
{
    [Fact]
    public async Task MigrationPreservesExistingAccountsLinksCodesPasskeysAndAllBusinessRows()
    {
        using var database = new AuthDatabaseFixture();
        await CreatePreviousSchemaAsync(database);
        var previousTables = database.Tables().Where(name => !name.StartsWith("__EF")).ToArray();
        var before = database.Snapshot(previousTables);
        await database.MigrateAsync();
        Assert.Equal(before, database.Snapshot(previousTables));
        Assert.Contains("auth_sessions", database.Tables());
        await database.MigrateAsync();
        Assert.Equal(before, database.Snapshot(previousTables));
        Assert.Equal(0L, database.Scalar("SELECT COUNT(*) FROM pragma_table_info('auth_sessions') WHERE lower(name) LIKE '%zone%'"));
        using var connection = database.OpenForeignKeyConnection();
        foreach (var sql in new[]
        {
            "INSERT INTO auth_sessions(IdHash,AuthUserId,CreatedAtUtc,ExpiresAtUtc,VerifiedAtUtc) VALUES(NULL,'existing','2026-10-25','2026-11-08','2026-10-25')",
            "INSERT INTO auth_sessions(IdHash,AuthUserId,CreatedAtUtc,ExpiresAtUtc,VerifiedAtUtc) VALUES(X'01',NULL,'2026-10-25','2026-11-08','2026-10-25')",
            "INSERT INTO auth_sessions(IdHash,AuthUserId,CreatedAtUtc,ExpiresAtUtc,VerifiedAtUtc) VALUES(X'01','missing','2026-10-25','2026-11-08','2026-10-25')"
        }) Assert.Equal(19, Assert.Throws<SqliteException>(() => AuthDatabaseFixture.Execute(connection, sql)).SqliteErrorCode);
        AuthDatabaseFixture.Execute(connection, "INSERT INTO auth_sessions(IdHash,AuthUserId,CreatedAtUtc,ExpiresAtUtc,VerifiedAtUtc) VALUES(X'01','existing','2026-10-25','2026-11-08','2026-10-25')");
        // 人物リンクとは別に、セッション自体もアカウントの連鎖削除を拒否する。
        AuthDatabaseFixture.Execute(connection, "DELETE FROM auth_user_links WHERE auth_user_id='existing'; DELETE FROM auth_enrollment_codes WHERE AuthUserId='existing'; DELETE FROM AspNetUserPasskeys WHERE UserId='existing'");
        Assert.Equal(19, Assert.Throws<SqliteException>(() => AuthDatabaseFixture.Execute(connection, "DELETE FROM AspNetUsers WHERE Id='existing'")).SqliteErrorCode);
    }

    [Fact]
    public async Task ConflictingSessionTableDoesNotDamagePreviouslyRegisteredData()
    {
        using var database = new AuthDatabaseFixture();
        await CreatePreviousSchemaAsync(database);
        database.Execute("CREATE TABLE auth_sessions(sentinel TEXT); INSERT INTO auth_sessions VALUES('keep')");
        var tables = database.Tables().Where(name => !name.StartsWith("__EF")).ToArray();
        var before = database.Snapshot(tables);
        await Assert.ThrowsAsync<SqliteException>(() => database.MigrateAsync());
        Assert.Equal(before, database.Snapshot(tables));
    }

    /// <summary>PR52時点の移行まで適用し、既存のアカウント・鍵・コードを保存する。</summary>
    private static async Task CreatePreviousSchemaAsync(AuthDatabaseFixture database)
    {
        await using var services = database.CreateServices();
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await context.GetService<IMigrator>().MigrateAsync("20261024233649_AddEnrollmentCodes");
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AuthUser>>();
        var user = new AuthUser { Id = "existing", UserName = "existing", IsEnabled = true };
        Assert.True((await users.CreateAsync(user)).Succeeded);
        Assert.True((await users.AddOrUpdatePasskeyAsync(user, new UserPasskeyInfo([1], [2], DateTimeOffset.UtcNow,
            0, [], true, false, false, [], []))).Succeeded);
        context.UserLinks.Add(new AuthUserLink { AuthUserId = user.Id, UserCode = 1 });
        context.EnrollmentCodes.Add(new EnrollmentCode
        {
            CodeHash = [1, 2, 3], AuthUserId = user.Id, CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10), ConsumedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
    }
}
