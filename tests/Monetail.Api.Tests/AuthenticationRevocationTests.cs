using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Authentication;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>全失効・緊急復旧・復元後リセットの原子性と既存データ保持を検証する。</summary>
public class AuthenticationRevocationTests
{
    [Fact]
    public async Task IndividualRevocationPreservesOtherAccountsAndBusinessDataWithoutIssuingCode()
    {
        await using var app = await LoginTestApp.CreateAsync();
        var other = await app.AddAccountAsync(2);
        using var admin = app.Client();
        using var target = app.Client();
        await app.LoginAsync(admin);
        var options = await app.BeginAsync(target);
        using var login = await app.VerifyAsync(target, app.AssertionFor(options, other.UserId, other.CredentialId));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        await SeedHistoryAsync(app, other.UserId);
        var protectedTables = ProtectedTables(app);
        var before = app.Database.Snapshot(protectedTables);
        var accounts = await AccountSnapshotAsync(app, other.UserId);
        var preserved = OtherAuthenticationSnapshot(app, app.UserId);
        var codeCount = app.Database.Scalar("SELECT COUNT(*) FROM auth_enrollment_codes");
        await RevokeAsync(app, 2);
        Assert.Equal(0L, app.Database.Scalar($"SELECT IsEnabled FROM AspNetUsers WHERE Id='{other.UserId}'"));
        Assert.Equal(0L, app.Database.Scalar($"SELECT COUNT(*) FROM AspNetUserPasskeys WHERE UserId='{other.UserId}'"));
        Assert.Equal(0L, app.Database.Scalar($"SELECT COUNT(*) FROM auth_sessions WHERE AuthUserId='{other.UserId}' AND RevokedAtUtc IS NULL"));
        Assert.Equal(0L, app.Database.Scalar($"SELECT COUNT(*) FROM auth_enrollment_codes WHERE AuthUserId='{other.UserId}' AND ConsumedAtUtc IS NULL AND RevokedAtUtc IS NULL"));
        Assert.Equal(codeCount, app.Database.Scalar("SELECT COUNT(*) FROM auth_enrollment_codes"));
        Assert.Equal(before, app.Database.Snapshot(protectedTables));
        Assert.Equal(accounts, await AccountSnapshotAsync(app));
        Assert.Equal(preserved, OtherAuthenticationSnapshot(app, app.UserId));
        Assert.Equal(1L, app.Database.Scalar($"SELECT COUNT(*) FROM auth_enrollment_codes WHERE AuthUserId='{other.UserId}' AND ConsumedAtUtc IS NOT NULL AND RevokedAtUtc IS NULL"));
        Assert.Equal(1L, app.Database.Scalar($"SELECT COUNT(*) FROM auth_sessions WHERE AuthUserId='{other.UserId}' AND RevokedAtUtc='2001-01-01 00:00:00'"));
        using var revoked = await target.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        using var unaffected = await admin.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.OK, unaffected.StatusCode);
        var pending = await app.BeginAsync(target);
        using var oldKey = await app.VerifyAsync(target, app.AssertionFor(pending, other.UserId, other.CredentialId));
        Assert.Equal(HttpStatusCode.Unauthorized, oldKey.StatusCode);
    }

    [Fact]
    public async Task LastAdministratorRequiresExplicitEmergencyRecoveryAndKeepsIdentityAndRoles()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        await app.LoginAsync(client);
        var original = app.Database.Snapshot(app.Database.Tables());
        await Assert.ThrowsAsync<InvalidOperationException>(() => RevokeAsync(app, 1));
        Assert.Equal(original, app.Database.Snapshot(app.Database.Tables()));
        var protectedTables = ProtectedTables(app);
        var before = app.Database.Snapshot(protectedTables);
        await RevokeAsync(app, 1, emergency: true);
        Assert.Equal(0L, app.Database.Scalar("SELECT IsEnabled FROM AspNetUsers"));
        Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUserPasskeys"));
        Assert.Equal(before, app.Database.Snapshot(protectedTables));
        var counts = app.Database.Snapshot(app.Database.Tables());
        app.Time.Advance(TimeSpan.FromHours(1));
        await RevokeAsync(app, 1, emergency: true);
        Assert.Equal(counts, app.Database.Snapshot(app.Database.Tables()));
        await using var scope = app.Factory.Services.CreateAsyncScope();
        var issued = await scope.ServiceProvider.GetRequiredService<AdminProvisioningService>().IssueEnrollmentCodeAsync(1);
        Assert.Equal(app.UserId, issued.AuthUserId);
        Assert.Equal(0L, app.Database.Scalar("SELECT IsEnabled FROM AspNetUsers"));
        Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUserRoles"));
        using var loggedOut = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, loggedOut.StatusCode);
        await LoginTestApp.CsrfAsync(client);
        using var enrollment = await client.PostAsJsonAsync("/auth/enrollment/options", new { code = issued.Code });
        Assert.Equal(HttpStatusCode.OK, enrollment.StatusCode);
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("disabled", false)]
    [InlineData("no-key", false)]
    [InlineData("no-person", false)]
    [InlineData("no-link", false)]
    [InlineData("not-admin", false)]
    public async Task NormalRevocationRequiresAnotherUsableAdministrator(string state, bool allowed)
    {
        await using var app = await LoginTestApp.CreateAsync();
        var other = await app.AddAccountAsync(2);
        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AuthUser>>();
            var user = (await users.FindByIdAsync(other.UserId))!;
            if (state != "not-admin") Assert.True((await users.AddToRoleAsync(user, "Admin")).Succeeded);
        }
        switch (state)
        {
            case "disabled": app.Database.Execute($"UPDATE AspNetUsers SET IsEnabled=0 WHERE Id='{other.UserId}'"); break;
            case "no-key": app.Database.Execute($"DELETE FROM AspNetUserPasskeys WHERE UserId='{other.UserId}'"); break;
            case "no-person": app.Database.Execute("DELETE FROM m_user WHERE code=2"); break;
            case "no-link": app.Database.Execute($"DELETE FROM auth_user_links WHERE auth_user_id='{other.UserId}'"); break;
        }
        var before = app.Database.Snapshot(app.Database.Tables());
        if (allowed)
        {
            await RevokeAsync(app, 1);
            Assert.Equal(0L, app.Database.Scalar($"SELECT IsEnabled FROM AspNetUsers WHERE Id='{app.UserId}'"));
            Assert.Equal(1L, app.Database.Scalar($"SELECT IsEnabled FROM AspNetUsers WHERE Id='{other.UserId}'"));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => RevokeAsync(app, 1));
            Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        }
    }

    [Fact]
    public async Task RestoreResetIncludesUnlinkedAndDisabledAccountsAndPreservesAllOtherData()
    {
        await using var app = await LoginTestApp.CreateAsync();
        var other = await app.AddAccountAsync(2);
        using var admin = app.Client();
        await app.LoginAsync(admin);
        await SeedHistoryAsync(app, app.UserId);
        await SeedHistoryAsync(app, other.UserId);
        app.Database.Execute($"DELETE FROM auth_user_links WHERE auth_user_id='{other.UserId}'");
        app.Database.Execute($"UPDATE AspNetUsers SET IsEnabled=0 WHERE Id='{other.UserId}'");
        var tables = ProtectedTables(app);
        var before = app.Database.Snapshot(tables);
        var accounts = await AccountSnapshotAsync(app, resetAll: true);
        var codeCount = app.Database.Scalar("SELECT COUNT(*) FROM auth_enrollment_codes");
        await ResetAsync(app);
        Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUsers WHERE IsEnabled<>0"));
        Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUserPasskeys"));
        Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM auth_sessions WHERE RevokedAtUtc IS NULL"));
        Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM auth_enrollment_codes WHERE ConsumedAtUtc IS NULL AND RevokedAtUtc IS NULL"));
        Assert.Equal(codeCount, app.Database.Scalar("SELECT COUNT(*) FROM auth_enrollment_codes"));
        Assert.Equal(accounts, await AccountSnapshotAsync(app));
        Assert.Equal(before, app.Database.Snapshot(tables));
        var after = app.Database.Snapshot(app.Database.Tables());
        app.Time.Advance(TimeSpan.FromDays(1));
        await ResetAsync(app);
        Assert.Equal(after, app.Database.Snapshot(app.Database.Tables()));
        using var oldCookie = await admin.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, oldCookie.StatusCode);
    }

    [Theory]
    [InlineData(false, "AspNetUsers", "UPDATE")]
    [InlineData(false, "AspNetUserPasskeys", "DELETE")]
    [InlineData(false, "auth_sessions", "UPDATE")]
    [InlineData(false, "auth_enrollment_codes", "UPDATE")]
    [InlineData(true, "AspNetUsers", "UPDATE")]
    [InlineData(true, "AspNetUserPasskeys", "DELETE")]
    [InlineData(true, "auth_sessions", "UPDATE")]
    [InlineData(true, "auth_enrollment_codes", "UPDATE")]
    public async Task AnyWriteFailureRollsBackWholeOperationAndAllowsRetry(bool all, string table, string operation)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        await app.LoginAsync(client);
        await SeedHistoryAsync(app, app.UserId);
        app.Database.Execute($"CREATE TRIGGER fail_revocation BEFORE {operation} ON {table} BEGIN SELECT RAISE(ABORT,'injected'); END");
        var before = app.Database.Snapshot(app.Database.Tables());
        await Assert.ThrowsAnyAsync<Exception>(() => all ? ResetAsync(app) : RevokeAsync(app, 1, emergency: true));
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        app.Database.Execute("DROP TRIGGER fail_revocation");
        if (all) await ResetAsync(app); else await RevokeAsync(app, 1, emergency: true);
        Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUserPasskeys"));
        Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM auth_sessions WHERE RevokedAtUtc IS NULL"));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("person-only")]
    [InlineData("missing-person")]
    [InlineData("missing-account")]
    public async Task InvalidTargetDoesNotChangeAnyData(string state)
    {
        await using var app = await LoginTestApp.CreateAsync();
        var personCode = state == "unknown" ? 999 : state == "person-only" ? 2 : 1;
        if (state == "missing-person") app.Database.Execute("DELETE FROM m_user WHERE code=1");
        if (state == "missing-account") app.Database.Execute("DELETE FROM AspNetUsers");
        var before = app.Database.Snapshot(app.Database.Tables());
        await Assert.ThrowsAsync<InvalidOperationException>(() => RevokeAsync(app, personCode, emergency: true));
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnmigratedDatabaseIsRejectedWithoutChangingLegacyData(bool all)
    {
        using var fixture = new AuthDatabaseFixture();
        var before = fixture.Snapshot(fixture.Tables());
        await using var services = fixture.CreateServices();
        await using var scope = services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<AuthenticationRevocationService>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => all ? service.ResetAfterRestoreAsync() : service.RevokeAccountAsync(1, true));
        Assert.Equal(before, fixture.Snapshot(fixture.Tables()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentAuthenticationCannotSurviveCompletedRevocation(bool reauthentication)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        JsonElement options;
        var path = reauthentication ? "/auth/reauthentication/verify" : "/auth/login/verify";
        if (reauthentication)
        {
            await app.LoginAsync(client);
            await LoginTestApp.CsrfAsync(client);
            using var begin = await client.PostAsJsonAsync("/auth/reauthentication/options", new { });
            Assert.Equal(HttpStatusCode.OK, begin.StatusCode);
            options = await begin.Content.ReadFromJsonAsync<JsonElement>();
        }
        else options = await app.BeginAsync(client);
        var verify = client.PostAsJsonAsync(path, new { credential = app.Assertion(options) });
        var revoke = Task.Run(() => RevokeAsync(app, 1, emergency: true));
        await Task.WhenAll(verify, revoke);
        using var result = await verify;
        Assert.Contains(result.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
        Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUsers WHERE IsEnabled<>0"));
        Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUserPasskeys"));
        Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM auth_sessions WHERE RevokedAtUtc IS NULL"));
        using var me = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task ConcurrentNormalRevocationsCannotDisableBothAdministrators()
    {
        await using var app = await LoginTestApp.CreateAsync();
        var other = await app.AddAccountAsync(2);
        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AuthUser>>();
            Assert.True((await users.AddToRoleAsync((await users.FindByIdAsync(other.UserId))!, "Admin")).Succeeded);
        }
        var results = await Task.WhenAll(new[] { 1, 2 }.Select(personCode => Task.Run(async () =>
        {
            try { await RevokeAsync(app, personCode); return true; }
            catch (InvalidOperationException) { return false; }
        })));
        Assert.Single(results, succeeded => succeeded);
        Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUsers WHERE IsEnabled=1"));
        Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUserPasskeys"));
        Assert.Equal(2L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUserRoles"));
    }

    /// <summary>期限切れ・取消済みセッションと、消費済み・未使用コードを合成する。</summary>
    private static async Task SeedHistoryAsync(LoginTestApp app, string userId)
    {
        await using var scope = app.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var old = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var revoked in new[] { false, true })
            context.Sessions.Add(new AuthSession
            {
                IdHash = RandomNumberGenerator.GetBytes(32), AuthUserId = userId, CreatedAtUtc = old,
                VerifiedAtUtc = old, ExpiresAtUtc = old.AddDays(1), RevokedAtUtc = revoked ? old : null
            });
        foreach (var state in new[] { "consumed", "revoked" })
            context.EnrollmentCodes.Add(new EnrollmentCode
            {
                CodeHash = RandomNumberGenerator.GetBytes(32), AuthUserId = userId, CreatedAtUtc = old,
                ExpiresAtUtc = old.AddMinutes(10), ConsumedAtUtc = state == "consumed" ? old : null,
                RevokedAtUtc = state == "revoked" ? old : null
            });
        if (!await context.EnrollmentCodes.AnyAsync(code => code.AuthUserId == userId && code.RevokedAtUtc == null && code.ConsumedAtUtc == null))
            context.EnrollmentCodes.Add(new EnrollmentCode
            {
                CodeHash = RandomNumberGenerator.GetBytes(32), AuthUserId = userId,
                CreatedAtUtc = old, ExpiresAtUtc = old.AddMinutes(10)
            });
        await context.SaveChangesAsync();
    }

    /// <summary>全失効の対象以外の表を、既存業務値・人物対応・ロールを含めて選ぶ。</summary>
    private static string[] ProtectedTables(LoginTestApp app) => app.Database.Tables().Where(table => table is not
        ("AspNetUsers" or "AspNetUserPasskeys" or "auth_sessions" or "auth_enrollment_codes")).ToArray();

    /// <summary>他アカウントの鍵・セッション・登録コードの保存値を比較する。</summary>
    private static string OtherAuthenticationSnapshot(LoginTestApp app, string userId) => string.Join("\n", new[]
    {
        app.Database.Scalar($"SELECT group_concat(hex(CredentialId)||Data) FROM AspNetUserPasskeys WHERE UserId='{userId}'"),
        app.Database.Scalar($"SELECT group_concat(hex(IdHash)||CreatedAtUtc||ExpiresAtUtc||VerifiedAtUtc||ifnull(RevokedAtUtc,'')) FROM auth_sessions WHERE AuthUserId='{userId}'"),
        app.Database.Scalar($"SELECT group_concat(hex(CodeHash)||CreatedAtUtc||ExpiresAtUtc||ifnull(ConsumedAtUtc,'')||ifnull(RevokedAtUtc,'')) FROM auth_enrollment_codes WHERE AuthUserId='{userId}'")
    });

    /// <summary>停止対象の利用可否だけを期待値へ変え、アカウントの他プロパティ保持を比較する。</summary>
    private static async Task<string> AccountSnapshotAsync(LoginTestApp app, string? disabledId = null, bool resetAll = false)
    {
        await using var scope = app.Factory.Services.CreateAsyncScope();
        var users = await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Users.AsNoTracking().OrderBy(user => user.Id).ToListAsync();
        foreach (var user in users) if (resetAll || user.Id == disabledId) user.IsEnabled = false;
        return JsonSerializer.Serialize(users);
    }

    /// <summary>CLIと同様に新しいスコープから対象アカウントを全失効する。</summary>
    private static async Task RevokeAsync(LoginTestApp app, int personCode, bool emergency = false)
    {
        await using var scope = app.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AuthenticationRevocationService>().RevokeAccountAsync(personCode, emergency);
    }

    /// <summary>CLIと同様に新しいスコープから復元後の全体リセットを行う。</summary>
    private static async Task ResetAsync(LoginTestApp app)
    {
        await using var scope = app.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AuthenticationRevocationService>().ResetAfterRestoreAsync();
    }
}
