using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Authentication;
using Monetail.Api.Persistence;
using Xunit;

namespace Monetail.Api.Tests;

public sealed class EnrollmentLinkTests
{
    [Theory]
    [InlineData("InvitationOnly", false)]
    [InlineData("Public", true)]
    public async Task ModeControlsPublicPageAndEntryButAlwaysAllowsInvitations(string mode, bool isPublic)
    {
        await using var app = await LoginTestApp.CreateAsync();
        await using var factory = app.NewFactory().WithWebHostBuilder(builder => builder.UseSetting("Registration:Mode", mode));
        using var client = app.Client(factory);
        using var login = await client.GetAsync("/auth/login");
        Assert.Equal(isPublic, (await login.Content.ReadAsStringAsync()).Contains("id=\"registration-link\""));
        using var register = await client.GetAsync("/auth/register");
        Assert.Equal(isPublic ? HttpStatusCode.OK : HttpStatusCode.Forbidden, register.StatusCode);
        using var invitation = await client.GetAsync("/auth/enroll");
        Assert.Equal(HttpStatusCode.OK, invitation.StatusCode);
        var html = await invitation.Content.ReadAsStringAsync();
        Assert.DoesNotContain("id=\"code\"", html);
        Assert.Contains("id=\"display-name\"", html);
        Assert.Contains("id=\"login\"", html);
        Assert.Equal("no-store", invitation.Headers.CacheControl!.ToString());
        await app.LoginAsync(client);
        var issued = await CreateAsync(client);
        using var enrollee = app.Client(factory);
        await LoginTestApp.CsrfAsync(enrollee);
        using var options = await enrollee.PostAsJsonAsync("/auth/enrollment/options", new
            { token = issued.GetProperty("enrollmentUrl").GetString()!.EnrollmentToken(), displayName = "name" });
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        using var registered = await enrollee.PostAsJsonAsync("/auth/enrollment/verify", new
            { credential = PasskeyEnrollmentTests.Credential(await options.Content.ReadFromJsonAsync<JsonElement>()) });
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
    }

    [Fact]
    public async Task OpeningLinkDoesNotConsumeItAndOldCodeDtoOrEndpointIsNotAccepted()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var admin = app.Client();
        await app.LoginAsync(admin);
        var target = await CreateAsync(admin);
        var token = target.GetProperty("enrollmentUrl").GetString()!.EnrollmentToken();
        Assert.StartsWith(LoginTestApp.Origin + "/auth/enroll#token=", target.GetProperty("enrollmentUrl").GetString());
        var before = app.Database.Snapshot(app.Database.Tables());
        using var client = app.Client();
        using var page = await client.GetAsync(target.GetProperty("enrollmentUrl").GetString());
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        await LoginTestApp.CsrfAsync(client);
        using var old = await client.PostAsJsonAsync("/auth/enrollment/options", new { code = token, displayName = "name" });
        Assert.Equal(HttpStatusCode.BadRequest, old.StatusCode);
        using var legacy = await admin.PostAsJsonAsync($"/admin/accounts/{target.GetProperty("authUserId").GetString()}/enrollment-code", new { });
        Assert.Equal(HttpStatusCode.NotFound, legacy.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task CancellationIsIdempotentAndInvalidatesAlreadyIssuedChallenge()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var admin = app.Client();
        await app.LoginAsync(admin);
        var target = await CreateAsync(admin);
        var id = target.GetProperty("authUserId").GetString();
        using var client = app.Client();
        await LoginTestApp.CsrfAsync(client);
        using var begun = await client.PostAsJsonAsync("/auth/enrollment/options", new { token = target.GetProperty("enrollmentUrl").GetString()!.EnrollmentToken(), displayName = "name" });
        var options = await begun.Content.ReadFromJsonAsync<JsonElement>();
        await LoginTestApp.CsrfAsync(admin);
        using var cancelled = await admin.DeleteAsync($"/admin/accounts/{id}/enrollment-invitation");
        Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        var before = app.Database.Snapshot(app.Database.Tables());
        using var again = await admin.DeleteAsync($"/admin/accounts/{id}/enrollment-invitation");
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        using var completed = await client.PostAsJsonAsync("/auth/enrollment/verify", new { credential = PasskeyEnrollmentTests.Credential(options) });
        Assert.Equal(HttpStatusCode.BadRequest, completed.StatusCode);
        Assert.Equal(0L, app.Database.Scalar($"SELECT IsEnabled FROM AspNetUsers WHERE Id='{id}'"));
        using var missing = await admin.DeleteAsync("/admin/accounts/unknown/enrollment-invitation");
        Assert.Equal(HttpStatusCode.NoContent, missing.StatusCode);
    }

    [Theory]
    [InlineData(EnrollmentCode.InvitationPurpose, true)]
    [InlineData(EnrollmentCode.InitialAdminPurpose, true)]
    [InlineData(EnrollmentCode.RecoveryPurpose, false)]
    [InlineData("unknown-purpose", false)]
    public async Task CancellationOnlyChangesNormalAndInitialAdminInvitations(string purpose, bool cancelled)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var admin = app.Client();
        await app.LoginAsync(admin);
        var target = await app.AddAccountAsync();
        IssuedEnrollmentInvitation issued;
        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AuthenticationRevocationService>().RevokeAccountAsync(target.UserId);
            issued = await scope.ServiceProvider.GetRequiredService<AdminProvisioningService>().IssueRecoveryLinkAsync(target.UserId);
            var database = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var link = await database.EnrollmentCodes.SingleAsync(row => row.AuthUserId == target.UserId);
            link.Purpose = purpose;
            await database.SaveChangesAsync();
        }
        var before = app.Database.Snapshot(app.Database.Tables());
        await LoginTestApp.CsrfAsync(admin);
        using var response = await admin.DeleteAsync($"/admin/accounts/{target.UserId}/enrollment-invitation");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(cancelled ? 1L : 0L, app.Database.Scalar($"SELECT COUNT(*) FROM auth_enrollment_codes WHERE AuthUserId='{target.UserId}' AND RevokedAtUtc IS NOT NULL"));
        if (!cancelled) Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        if (purpose == EnrollmentCode.RecoveryPurpose)
        {
            using var client = app.Client();
            await LoginTestApp.CsrfAsync(client);
            using var options = await client.PostAsJsonAsync("/auth/enrollment/options", new { token = issued.Token(), displayName = "復旧" });
            Assert.Equal(HttpStatusCode.OK, options.StatusCode);
            using var verified = await client.PostAsJsonAsync("/auth/enrollment/verify", new
                { credential = PasskeyEnrollmentTests.Credential(await options.Content.ReadFromJsonAsync<JsonElement>()) });
            Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        }
    }

    [Theory]
    [InlineData("session")]
    [InlineData("role")]
    public async Task CancellationRollsBackWhenAdministratorChangesDuringWrite(string change)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var admin = app.Client();
        await app.LoginAsync(admin);
        var target = await CreateAsync(admin);
        var mutation = change == "session" ? "UPDATE auth_sessions SET RevokedAtUtc=CreatedAtUtc" : "DELETE FROM AspNetUserRoles";
        app.Database.Execute($"CREATE TRIGGER change_admin AFTER UPDATE OF RevokedAtUtc ON auth_enrollment_codes BEGIN {mutation}; END");
        var before = app.Database.Snapshot(app.Database.Tables());
        await LoginTestApp.CsrfAsync(admin);
        using var cancelled = await admin.DeleteAsync($"/admin/accounts/{target.GetProperty("authUserId").GetString()}/enrollment-invitation");
        Assert.Equal(HttpStatusCode.Forbidden, cancelled.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationRequiresCsrfAndRecentAdministrator(bool stale)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var admin = app.Client();
        await app.LoginAsync(admin);
        var target = await CreateAsync(admin);
        await LoginTestApp.CsrfAsync(admin);
        if (stale) app.Time.Advance(TimeSpan.FromMinutes(5));
        else admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        var before = app.Database.Snapshot(app.Database.Tables());
        using var cancelled = await admin.DeleteAsync($"/admin/accounts/{target.GetProperty("authUserId").GetString()}/enrollment-invitation");
        Assert.Equal(stale ? HttpStatusCode.Forbidden : HttpStatusCode.BadRequest, cancelled.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task GeneralInvitationExpiresAtSevenDaysIncludingAfterRestart()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var admin = app.Client();
        await app.LoginAsync(admin);
        var target = await CreateAsync(admin);
        Assert.Equal(app.Time.GetUtcNow().AddDays(7), target.GetProperty("expiresAtUtc").GetDateTimeOffset());
        app.Time.Advance(TimeSpan.FromDays(7));
        await using var factory = app.NewFactory();
        using var client = app.Client(factory);
        await LoginTestApp.CsrfAsync(client);
        using var expired = await client.PostAsJsonAsync("/auth/enrollment/options", new { token = target.GetProperty("enrollmentUrl").GetString()!.EnrollmentToken(), displayName = "name" });
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
    }

    [Fact]
    public async Task MigrationRetiresUnusedLegacyTokensButKeepsConsumedHistoryAccountsAndKeys()
    {
        using var db = new AuthDatabaseFixture();
        await using (var services = db.CreateServices())
        await using (var scope = services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            await context.GetService<IMigrator>().MigrateAsync("20261103221508_AddSelfRegistrationCompletions");
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AuthUser>>();
            Assert.True((await users.CreateAsync(new AuthUser { Id = "pending", UserName = "pending" })).Succeeded);
            context.EnrollmentCodes.AddRange(
                new() { AuthUserId = "pending", Purpose = "passkey-enrollment", CodeHash = [1], CreatedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10) },
                new() { AuthUserId = "pending", Purpose = "passkey-enrollment", CodeHash = [2], CreatedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow, ConsumedAtUtc = DateTime.UtcNow });
            await context.SaveChangesAsync();
        }
        // このテストは招待方式移行だけを検証し、人物を含む既存表をすべて比較する。
        var names = db.Tables().Where(name => name is not "auth_enrollment_codes" and not "__MonetailMigrationsHistory");
        var before = db.Snapshot(names);
        await db.MigrateToAsync("20261104010000_RetireEnrollmentCodes");
        Assert.Equal(before, db.Snapshot(names));
        Assert.Equal(1L, db.Scalar("SELECT COUNT(*) FROM auth_enrollment_codes WHERE CodeHash=X'01' AND RevokedAtUtc IS NOT NULL"));
        Assert.Equal(1L, db.Scalar("SELECT COUNT(*) FROM auth_enrollment_codes WHERE CodeHash=X'02' AND ConsumedAtUtc IS NOT NULL AND RevokedAtUtc IS NULL"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CliRecoveryPreservesDisplayNameIncludingLegacyNull(bool missingName)
    {
        await using var app = await LoginTestApp.CreateAsync();
        // 旧表示名がNULLでも、実際に登録済みだった履歴は保持する。
        if (missingName) app.Database.Execute("UPDATE AspNetUsers SET DisplayName=NULL; UPDATE auth_enrollment_codes SET ConsumedAtUtc=CreatedAtUtc");
        IssuedEnrollmentInvitation issued;
        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AuthenticationRevocationService>().RevokeAccountAsync(app.UserId, emergencyRecovery: true);
            issued = await scope.ServiceProvider.GetRequiredService<AdminProvisioningService>().IssueRecoveryLinkAsync(app.UserId);
        }
        using var client = app.Client();
        await LoginTestApp.CsrfAsync(client);
        using var options = await client.PostAsJsonAsync("/auth/enrollment/options", new { token = issued.Token(), displayName = "書き換え要求" });
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        using var verified = await client.PostAsJsonAsync("/auth/enrollment/verify", new
            { credential = PasskeyEnrollmentTests.Credential(await options.Content.ReadFromJsonAsync<JsonElement>()) });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        var name = app.Database.Scalar("SELECT DisplayName FROM AspNetUsers");
        if (missingName) Assert.IsType<DBNull>(name);
        else Assert.Equal("同じ名前", name);
        Assert.Equal(app.UserId, app.Database.Scalar("SELECT Id FROM AspNetUsers"));
        Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUserRoles"));
        Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM auth_sessions"));
    }

    private static async Task<JsonElement> CreateAsync(HttpClient admin)
    {
        await LoginTestApp.CsrfAsync(admin);
        using var response = await admin.PostAsJsonAsync("/admin/accounts", new { requestId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
