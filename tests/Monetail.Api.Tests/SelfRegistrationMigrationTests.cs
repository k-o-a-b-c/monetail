using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Authentication;
using Monetail.Api.Persistence;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>既存APIの鍵を保持する明示移行と、アカウントから独立した成功記録を確認する。</summary>
public sealed class SelfRegistrationMigrationTests
{
    [Fact]
    public async Task UpgradeAddsOnlyCompletionStorageAndPreservesExistingUsersPasskeysAndBusinessRows()
    {
        using var db = new AuthDatabaseFixture();
        await using (var services = db.CreateServices())
        await using (var scope = services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            await context.GetService<IMigrator>().MigrateAsync("20261027104007_InitializeApiDatabase");
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AuthUser>>();
            var user = new AuthUser { Id = "existing-account", UserName = "existing-account", DisplayName = "既存名", IsEnabled = true };
            Assert.True((await users.CreateAsync(user)).Succeeded);
            var passkey = new UserPasskeyInfo([1, 2, 3], [4, 5, 6], DateTimeOffset.UtcNow, 0,
                ["internal"], true, false, false, [], []) { Name = "existing-key" };
            Assert.True((await users.AddOrUpdatePasskeyAsync(user, passkey)).Succeeded);
        }
        // ledger_people receives an added payment_order_revision column in a later migration.
        var tables = db.Tables().Where(name => !name.StartsWith("__", StringComparison.Ordinal) && name != "ledger_people").ToArray();
        var before = db.Snapshot(tables);
        await db.MigrateAsync();
        Assert.Equal(before, db.Snapshot(tables));
        Assert.Equal(0L, db.Scalar("SELECT COUNT(*) FROM auth_self_registration_completions"));
        Assert.Equal(0L, db.Scalar("SELECT COUNT(*) FROM pragma_foreign_key_list('auth_self_registration_completions')"));
        Assert.Equal(5L, db.Scalar("SELECT COUNT(*) FROM __MonetailMigrationsHistory"));
        await db.MigrateAsync();
        Assert.Equal(before, db.Snapshot(tables));
    }

    [Fact]
    public async Task MissingMigrationFailsClosedAndNeverCreatesTheTableOnHttpRequests()
    {
        await using var app = await SelfRegistrationTestApp.CreateAsync();
        app.Database.Execute("DROP TABLE auth_self_registration_completions; DELETE FROM __MonetailMigrationsHistory WHERE MigrationId LIKE '%AddSelfRegistrationCompletions'");
        using var client = app.Client();
        await LoginTestApp.CsrfAsync(client);
        using var response = await client.PostAsJsonAsync(SelfRegistrationTestApp.Root + "/options", new { displayName = "name" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("auth_self_registration_completions", app.Database.Tables());
        Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUsers"));
    }

    [Fact]
    public async Task DeletingAnAccountDoesNotEraseTheSuccessBudget()
    {
        await using var app = await SelfRegistrationTestApp.CreateAsync();
        using var client = app.Client();
        var options = await SelfRegistrationTestApp.BeginAsync(client);
        using var registered = await SelfRegistrationTestApp.VerifyAsync(client, PasskeyEnrollmentTests.Credential(options));
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        using var connection = app.Database.OpenForeignKeyConnection();
        AuthDatabaseFixture.Execute(connection, "DELETE FROM AspNetUsers");
        Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUserPasskeys"));
        Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM auth_self_registration_completions"));
    }
}
