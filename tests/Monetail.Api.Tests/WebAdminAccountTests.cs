using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Authentication;
using Monetail.Api.Features.Accounts;
using Monetail.Api.Persistence;
using Xunit;

namespace Monetail.Api.Tests;

public sealed class WebAdminAccountTests
{
    [Theory]
    [InlineData("InvitationOnly")]
    [InlineData("Public")]
    public async Task ManagementIsAvailableToAdminInBothRegistrationModes(string mode)
    {
        await using var app = await LoginTestApp.CreateAsync();
        await using var factory = app.NewFactory().WithWebHostBuilder(builder => builder.UseSetting("Registration:Mode", mode));
        using var client = app.Client(factory);
        await app.LoginAsync(client);
        using var response = await client.GetAsync("/admin/account-management");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<dialog", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("anonymous", 401)]
    [InlineData("ordinary", 403)]
    [InlineData("admin", 200)]
    public async Task HtmlScriptAndListRequireCurrentGlobalAdminWithoutRecentVerification(string actor, int status)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        if (actor != "anonymous") await app.LoginAsync(client);
        if (actor == "ordinary") app.Database.Execute("DELETE FROM AspNetUserRoles");
        app.Time.Advance(TimeSpan.FromMinutes(6));
        foreach (var path in new[] { "/admin/account-management", "/admin/account-management.js", "/admin/accounts" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal((HttpStatusCode)status, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
            if (status != 200) Assert.DoesNotContain("<dialog", await response.Content.ReadAsStringAsync());
        }
        if (actor != "anonymous")
        {
            var me = await client.GetFromJsonAsync<JsonElement>("/auth/me");
            Assert.Equal(actor == "admin", me.GetProperty("isGlobalAdmin").GetBoolean());
            Assert.False(me.TryGetProperty("adminNote", out _));
        }
    }

    [Theory]
    [InlineData("normal", "invited", true)]
    [InlineData("initial", "invited", true)]
    [InlineData("recovery", "unregistered", false)]
    [InlineData("unknown", "unregistered", false)]
    [InlineData("expired", "unregistered", false)]
    [InlineData("revoked", "unregistered", false)]
    [InlineData("consumed", "suspended", false)]
    [InlineData("session", "suspended", false)]
    [InlineData("named", "suspended", false)]
    [InlineData("active", "active", false)]
    public async Task ListDerivesStateFromHistoryAndOnlyLiveInvitationPurposes(string scenario, string expected, bool expiry)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        await app.LoginAsync(client);
        var id = Guid.NewGuid().ToString("N");
        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AuthUser>>();
            Assert.True((await users.CreateAsync(new AuthUser { Id = id, UserName = id,
                IsEnabled = scenario == "active", DisplayName = scenario == "named" ? "本人名" : null })).Succeeded);
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var now = app.Time.GetUtcNow().UtcDateTime;
            db.Set<AccountAdminNote>().Add(new() { AuthUserId = id, AdminNote = "招待先メモ" });
            db.EnrollmentCodes.Add(new() { CodeHash = RandomNumberGenerator.GetBytes(32), AuthUserId = id,
                Purpose = scenario switch { "initial" => EnrollmentCode.InitialAdminPurpose, "recovery" => EnrollmentCode.RecoveryPurpose,
                    "unknown" => "unknown", _ => EnrollmentCode.InvitationPurpose },
                CreatedAtUtc = now, ExpiresAtUtc = scenario == "expired" ? now : now.AddDays(7),
                ConsumedAtUtc = scenario == "consumed" ? now : null, RevokedAtUtc = scenario == "revoked" ? now : null });
            if (scenario == "session") db.Sessions.Add(new() { IdHash = RandomNumberGenerator.GetBytes(32), AuthUserId = id,
                CreatedAtUtc = now, VerifiedAtUtc = now, ExpiresAtUtc = now, RevokedAtUtc = now });
            await db.SaveChangesAsync();
        }
        var before = app.Database.Snapshot(app.Database.Tables());
        var rows = await client.GetFromJsonAsync<JsonElement[]>("/admin/accounts");
        var target = rows!.Single(row => row.GetProperty("authUserId").GetString() == id);
        Assert.Equal(expected, target.GetProperty("registrationStatus").GetString());
        Assert.Equal("招待先メモ", target.GetProperty("adminNote").GetString());
        Assert.False(target.GetProperty("isGlobalAdmin").GetBoolean());
        if (expiry) Assert.Equal(app.Time.GetUtcNow().AddDays(7), target.GetProperty("invitationExpiresAtUtc").GetDateTimeOffset());
        else Assert.Equal(JsonValueKind.Null, target.GetProperty("invitationExpiresAtUtc").ValueKind);
        Assert.True(rows!.Single(row => row.GetProperty("authUserId").GetString() == app.UserId).GetProperty("isGlobalAdmin").GetBoolean());
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("active")]
    [InlineData("suspended")]
    public async Task NoteCanBeEditedAndClearedWithoutChangingAuthenticationOrInvitations(string state)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        await app.LoginAsync(client);
        var id = state == "pending" ? (await Create(client, "初期メモ")).GetProperty("authUserId").GetString()! : (await app.AddAccountAsync()).UserId;
        if (state == "suspended") app.Database.Execute($"UPDATE AspNetUsers SET IsEnabled=0 WHERE Id='{id}'");
        var tables = app.Database.Tables().Where(table => table != "auth_account_admin_notes").ToArray();
        var before = app.Database.Snapshot(tables);
        foreach (var note in new[] { new string('あ', 100), "重複可", "", null })
        {
            using var response = await Note(client, id, note);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            var rows = await client.GetFromJsonAsync<JsonElement[]>("/admin/accounts");
            Assert.Equal(string.IsNullOrEmpty(note) ? null : note, rows!.Single(row => row.GetProperty("authUserId").GetString() == id).GetProperty("adminNote").GetString());
            Assert.Equal(before, app.Database.Snapshot(tables));
        }
        var saved = app.Database.Snapshot(app.Database.Tables());
        using var invalid = await Note(client, id, new string('a', 101));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(saved, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task CreationReplayDoesNotOverwriteNoteOrRedisplayUrlAndRejectsOversizedNote()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        await app.LoginAsync(client);
        await LoginTestApp.CsrfAsync(client);
        var requestId = Guid.NewGuid();
        using var first = await client.PostAsJsonAsync("/admin/accounts", new { requestId, adminNote = new string('あ', 100) });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var original = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotNull(original.GetProperty("enrollmentUrl").GetString());
        var before = app.Database.Snapshot(app.Database.Tables());
        using var replay = await client.PostAsJsonAsync("/admin/accounts", new { requestId, adminNote = "変更しない" });
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var body = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(original.GetProperty("authUserId").GetString(), body.GetProperty("authUserId").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("enrollmentUrl").ValueKind);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        using var invalid = await client.PostAsJsonAsync("/admin/accounts", new { requestId = Guid.NewGuid(), adminNote = new string('a', 101) });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Theory]
    [InlineData("anonymous", 401)]
    [InlineData("ordinary", 403)]
    [InlineData("stale", 403)]
    [InlineData("csrf", 400)]
    [InlineData("origin", 403)]
    [InlineData("unknown", 404)]
    [InlineData("fields", 400)]
    public async Task NoteEndpointEnforcesAuthorizationAndInputWithoutWrites(string failure, int status)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        if (failure != "anonymous") await app.LoginAsync(client);
        await LoginTestApp.CsrfAsync(client);
        if (failure == "ordinary") app.Database.Execute("DELETE FROM AspNetUserRoles");
        if (failure == "stale") app.Time.Advance(TimeSpan.FromMinutes(5));
        if (failure == "csrf") client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        if (failure == "origin") client.DefaultRequestHeaders.Remove("Origin");
        var before = app.Database.Snapshot(app.Database.Tables());
        using var response = failure == "fields"
            ? await client.PatchAsJsonAsync($"/admin/accounts/{app.UserId}/admin-note", new { adminNote = "メモ", role = "Admin" })
            : await client.PatchAsJsonAsync($"/admin/accounts/{(failure == "unknown" ? "missing" : app.UserId)}/admin-note", new { adminNote = "メモ" });
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Theory]
    [InlineData("role", false, false)]
    [InlineData("session", false, false)]
    [InlineData("verification", false, false)]
    [InlineData("role", true, false)]
    [InlineData("role", false, true)]
    public async Task NoteAndAccountCreationRollBackWhenAuthorizationChangesDuringSave(string change, bool create, bool existing)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        await app.LoginAsync(client);
        await LoginTestApp.CsrfAsync(client);
        if (existing)
        {
            using var initial = await Note(client, app.UserId, "変更前");
            Assert.Equal(HttpStatusCode.NoContent, initial.StatusCode);
        }
        var mutation = change switch { "role" => "DELETE FROM AspNetUserRoles",
            "session" => "UPDATE auth_sessions SET RevokedAtUtc=CreatedAtUtc",
            _ => "UPDATE auth_sessions SET VerifiedAtUtc=datetime(VerifiedAtUtc,'-5 minutes')" };
        app.Database.Execute($"CREATE TRIGGER change_admin AFTER {(existing ? "UPDATE" : "INSERT")} ON auth_account_admin_notes BEGIN {mutation}; END");
        var before = app.Database.Snapshot(app.Database.Tables());
        using var response = create ? await client.PostAsJsonAsync("/admin/accounts", new { requestId = Guid.NewGuid(), adminNote = "未保存" })
            : await client.PatchAsJsonAsync($"/admin/accounts/{app.UserId}/admin-note", new { adminNote = "未保存" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task ExactFiveMinuteBoundaryRequiresInPlaceWebAuthnThenAllowsOriginalUpdate()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        await app.LoginAsync(client);
        var loginTime = app.Time.GetUtcNow();
        app.Time.Set(loginTime.AddMinutes(5).AddTicks(-1));
        using var allowed = await Note(client, app.UserId, "保存可");
        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        app.Time.Set(loginTime.AddMinutes(5));
        using var denied = await Note(client, app.UserId, "再確認後");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var optionsResponse = await client.PostAsJsonAsync("/auth/reauthentication/options", new { });
        var options = await optionsResponse.Content.ReadFromJsonAsync<JsonElement>();
        using var verified = await client.PostAsJsonAsync("/auth/reauthentication/verify", new { credential = app.Assertion(options, count: 1) });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        using var updated = await Note(client, app.UserId, "再確認後");
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
    }

    [Fact]
    public async Task MigrationPreservesAllExistingTablesAndStartsWithNoNotes()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        await app.LoginAsync(client);
        await using (var scope = app.Factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ApiDbContext>().GetService<IMigrator>().MigrateAsync("20261104010000_RetireEnrollmentCodes");
        // The payment migration extends ledger_people; compare other existing tables unchanged.
        var tables = app.Database.Tables().Where(table => table != ApiDbContext.HistoryTable && table != "ledger_people").ToArray();
        var before = app.Database.Snapshot(tables);
        await app.Database.MigrateAsync();
        Assert.Equal(before, app.Database.Snapshot(tables));
        Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM auth_account_admin_notes"));
        await using (var scope = app.Factory.Services.CreateAsyncScope())
            Assert.False(scope.ServiceProvider.GetRequiredService<ApiDbContext>().Database.HasPendingModelChanges());
        await app.Database.MigrateAsync();
        Assert.Equal(before, app.Database.Snapshot(tables));
    }

    [Fact]
    public async Task ConcurrentNoteUpdatesRemainAtomicAndDuplicatesAreAllowed()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        await app.LoginAsync(client);
        await LoginTestApp.CsrfAsync(client);
        var responses = await Task.WhenAll(client.PatchAsJsonAsync($"/admin/accounts/{app.UserId}/admin-note", new { adminNote = "同じ" }),
            client.PatchAsJsonAsync($"/admin/accounts/{app.UserId}/admin-note", new { adminNote = "別のメモ" }));
        foreach (var response in responses) { Assert.Equal(HttpStatusCode.NoContent, response.StatusCode); response.Dispose(); }
        Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM auth_account_admin_notes"));
        await Create(client, "同じ");
        using var saved = await Note(client, app.UserId, "同じ");
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        Assert.Equal(2L, app.Database.Scalar("SELECT COUNT(*) FROM auth_account_admin_notes WHERE AdminNote='同じ'"));
    }

    private static async Task<HttpResponseMessage> Note(HttpClient client, string id, string? note)
    {
        await LoginTestApp.CsrfAsync(client);
        return await client.PatchAsJsonAsync($"/admin/accounts/{id}/admin-note", new { adminNote = note });
    }
    private static async Task<JsonElement> Create(HttpClient client, string note)
    {
        await LoginTestApp.CsrfAsync(client);
        using var response = await client.PostAsJsonAsync("/admin/accounts", new { requestId = Guid.NewGuid(), adminNote = note });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
