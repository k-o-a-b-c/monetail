using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Authentication;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>要求記録テーブルの追加移行と、既存認証・業務データの保持を検証する。</summary>
public class AccountCreationMigrationTests
{
    [Fact]
    public async Task MigrationAddsOnlyTheRequestTableAndEnforcesItsIdentityAndForeignKeys()
    {
        await using var app = await LoginTestApp.CreateAsync();
        var target = await app.AddAccountAsync(2);
        using var client = app.Client();
        await app.LoginAsync(client);
        await DowngradeAsync(app);
        var tables = app.Database.Tables().Where(name => !name.StartsWith("__EF", StringComparison.Ordinal)).ToArray();
        var before = app.Database.Snapshot(tables);
        await app.Database.MigrateAsync();
        Assert.Equal(new[] { "auth_account_creation_requests" }, app.Database.Tables().Except(tables).Where(name => !name.StartsWith("__EF", StringComparison.Ordinal)).ToArray());
        Assert.Equal(before, app.Database.Snapshot(tables));
        await app.Database.MigrateAsync();
        Assert.Equal(before, app.Database.Snapshot(tables));
        using var connection = app.Database.OpenForeignKeyConnection();
        var insert = $"INSERT INTO auth_account_creation_requests VALUES('request','{app.UserId}','{target.UserId}',2,'2026-10-26')";
        foreach (var sql in new[]
        {
            insert.Replace("'request'", "NULL"),
            insert.Replace($"'{app.UserId}'", "NULL"),
            insert.Replace($"'{target.UserId}'", "'missing'"),
            insert.Replace(",2,", ",999,")
        }) Assert.Equal(19, Assert.Throws<SqliteException>(() => AuthDatabaseFixture.Execute(connection, sql)).SqliteErrorCode);
        AuthDatabaseFixture.Execute(connection, insert);
        Assert.Equal(19, Assert.Throws<SqliteException>(() => AuthDatabaseFixture.Execute(connection, insert)).SqliteErrorCode);
        Assert.Equal(19, Assert.Throws<SqliteException>(() => AuthDatabaseFixture.Execute(connection, insert.Replace("'request'", "'another'"))).SqliteErrorCode);
        var all = app.Database.Tables();
        var allBefore = app.Database.Snapshot(all);
        await app.Database.MigrateAsync();
        Assert.Equal(allBefore, app.Database.Snapshot(all));
    }

    [Fact]
    public async Task ConflictingTableDoesNotDamageExistingDataAndMigrationCanBeRetried()
    {
        await using var app = await LoginTestApp.CreateAsync();
        await DowngradeAsync(app);
        app.Database.Execute("CREATE TABLE auth_account_creation_requests(sentinel TEXT); INSERT INTO auth_account_creation_requests VALUES('keep')");
        var tables = app.Database.Tables().Where(name => !name.StartsWith("__EF", StringComparison.Ordinal)).ToArray();
        var before = app.Database.Snapshot(tables);
        await Assert.ThrowsAsync<SqliteException>(() => app.Database.MigrateAsync());
        Assert.Equal(before, app.Database.Snapshot(tables));
        app.Database.Execute("DROP TABLE auth_account_creation_requests");
        await app.Database.MigrateAsync();
        Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM auth_account_creation_requests"));
    }

    /// <summary>要求記録テーブルを追加する前の既存認証スキーマへ戻す。</summary>
    private static async Task DowngradeAsync(LoginTestApp app)
    {
        await using var scope = app.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AuthDbContext>().GetService<IMigrator>().MigrateAsync("20261025080829_AddSessions");
    }
}
