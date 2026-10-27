using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Authentication;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>人物名を補完しない追加移行と、旧アカウント・発行履歴・全業務値の保持を検証する。</summary>
public class AccountIdentityMigrationTests
{
    [Fact]
    public async Task MigrationDoesNotCopyNamesOrRewriteStoredAuthenticationHistoryAndBusinessData()
    {
        await using var app = await LoginTestApp.CreateAsync();
        var other = await app.AddAccountAsync(2);
        using var client = app.Client();
        await app.LoginAsync(client);
        await using (var scope = app.Factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AuthDbContext>().GetService<IMigrator>()
                .MigrateAsync("20261026064157_AddAccountCreationRequests");
        app.Database.Execute($"INSERT INTO auth_account_creation_requests(RequestId,CreatedByAuthUserId,AuthUserId,UserCode,CreatedAtUtc) VALUES('old-request','{app.UserId}','{other.UserId}',2,'2026-10-26')");
        var preserved = app.Database.Tables().Where(table => table is not ("AspNetUsers" or "auth_account_creation_requests") && !table.StartsWith("__EF")).ToArray();
        var before = app.Database.Snapshot(preserved);
        var usersBefore = Rows(app.Database, "AspNetUsers", "Id");
        var requestsBefore = Rows(app.Database, "auth_account_creation_requests", "RequestId");
        await app.Database.MigrateAsync();
        Assert.Equal(before, app.Database.Snapshot(preserved));
        Assert.Equal(usersBefore, Rows(app.Database, "AspNetUsers", "Id"));
        Assert.Equal(requestsBefore, Rows(app.Database, "auth_account_creation_requests", "RequestId"));
        Assert.Equal(2L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUsers WHERE DisplayName IS NULL"));
        Assert.Equal(0L, app.Database.Scalar("SELECT \"notnull\" FROM pragma_table_info('auth_account_creation_requests') WHERE name='UserCode'"));
        var all = app.Database.Tables();
        var migrated = app.Database.Snapshot(all);
        await app.Database.MigrateAsync();
        Assert.Equal(migrated, app.Database.Snapshot(all));
        await using var freshScope = app.Factory.Services.CreateAsyncScope();
        var manager = freshScope.ServiceProvider.GetRequiredService<UserManager<AuthUser>>();
        var unlinked = new AuthUser { UserName = Guid.NewGuid().ToString("N") };
        Assert.True((await manager.CreateAsync(unlinked)).Succeeded);
        using var connection = app.Database.OpenForeignKeyConnection();
        AuthDatabaseFixture.Execute(connection, $"INSERT INTO auth_account_creation_requests(RequestId,CreatedByAuthUserId,AuthUserId,UserCode,CreatedAtUtc) VALUES('new-request','{app.UserId}','{unlinked.Id}',NULL,'2026-10-27')");
        Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM auth_account_creation_requests WHERE RequestId='new-request' AND UserCode IS NULL"));
        Assert.Equal(0L, app.Database.Scalar($"SELECT COUNT(*) FROM auth_user_links WHERE auth_user_id='{unlinked.Id}'"));
    }

    /// <summary>表示名以外の全保存列を、SQLiteの保存型も含めて比較する。</summary>
    private static string Rows(AuthDatabaseFixture database, string table, string key)
    {
        using var connection = database.OpenForeignKeyConnection();
        using var columnsCommand = connection.CreateCommand();
        columnsCommand.CommandText = $"PRAGMA table_info(\"{table}\")";
        var columns = new List<string>();
        using (var reader = columnsCommand.ExecuteReader())
            while (reader.Read()) if (reader.GetString(1) != "DisplayName") columns.Add(reader.GetString(1));
        using var command = connection.CreateCommand();
        var values = string.Join(",", columns.SelectMany(column => new[] { $"typeof(\"{column}\")", $"\"{column}\"" }));
        command.CommandText = $"SELECT {values} FROM \"{table}\" ORDER BY \"{key}\"";
        var rows = new List<object?[]>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var row = new object?[reader.FieldCount];
                for (var i = 0; i < row.Length; i++) row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(row);
            }
        }
        return JsonSerializer.Serialize(rows);
    }
}
