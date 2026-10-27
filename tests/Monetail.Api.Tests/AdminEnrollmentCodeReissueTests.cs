using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Authentication;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>一般・管理者アカウントのコード再発行と、実行者・対象条件・原子的な失効を検証する。</summary>
public class AdminEnrollmentCodeReissueTests
{
    [Fact]
    public async Task ReissuePreservesAccountsAndSessionsAndTheNewCodeCanEnrollTheSameAccount()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var admin = app.Client();
        var target = await PrepareAsync(app, admin);
        var tables = app.Database.Tables().Except(new[] { "auth_enrollment_codes" }).ToArray();
        var before = app.Database.Snapshot(tables);
        using var issued = await ReissueAsync(admin, target.Id);
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        Assert.False(issued.Headers.Contains("Set-Cookie"));
        Assert.Equal("no-store", issued.Headers.CacheControl!.ToString());
        var body = await issued.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(new[] { "authUserId", "code", "expiresAtUtc" }, body.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal(target.Id, body.GetProperty("authUserId").GetString());
        var code = body.GetProperty("code").GetString()!;
        Assert.Equal(43, code.Length);
        Assert.NotEqual(target.Code, code);
        Assert.Equal(app.Time.GetUtcNow().AddMinutes(10), body.GetProperty("expiresAtUtc").GetDateTimeOffset());
        Assert.Equal(1L, app.Database.Scalar($"SELECT COUNT(*) FROM auth_enrollment_codes WHERE CodeHash=X'{Hash(target.Code)}' AND RevokedAtUtc IS NOT NULL"));
        Assert.Equal(1L, app.Database.Scalar($"SELECT COUNT(*) FROM auth_enrollment_codes WHERE CodeHash=X'{Hash(code)}' AND AuthUserId='{target.Id}' AND RevokedAtUtc IS NULL"));
        Assert.DoesNotContain(code, app.Database.Snapshot(app.Database.Tables()));
        Assert.Equal(before, app.Database.Snapshot(tables));
        using var enrollee = app.Client();
        await LoginTestApp.CsrfAsync(enrollee);
        using var rejected = await enrollee.PostAsJsonAsync("/auth/enrollment/options", new { code = target.Code, displayName = "新しいアカウント名" });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        using var begin = await enrollee.PostAsJsonAsync("/auth/enrollment/options", new { code, displayName = "新しいアカウント名" });
        Assert.Equal(HttpStatusCode.OK, begin.StatusCode);
        var options = await begin.Content.ReadFromJsonAsync<JsonElement>();
        using var complete = await enrollee.PostAsJsonAsync("/auth/enrollment/verify", new { credential = app.Attestation(options, RandomNumberGenerator.GetBytes(32)) });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        Assert.Equal(1L, app.Database.Scalar($"SELECT IsEnabled FROM AspNetUsers WHERE Id='{target.Id}'"));
        Assert.Equal(0L, app.Database.Scalar($"SELECT COUNT(*) FROM auth_user_links WHERE auth_user_id='{target.Id}'"));
        using var anonymous = await enrollee.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullyRevokedAccountCanReceiveANewCodeWithoutRevivingItsOldSessions(bool targetIsAdmin)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var admin = app.Client();
        var target = await PrepareAsync(app, admin);
        if (targetIsAdmin)
        {
            await using var scope = app.Factory.Services.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AuthUser>>();
            Assert.True((await users.AddToRoleAsync((await users.FindByIdAsync(target.Id))!, "Admin")).Succeeded);
        }
        using var enrollee = app.Client();
        await LoginTestApp.CsrfAsync(enrollee);
        using var begin = await enrollee.PostAsJsonAsync("/auth/enrollment/options", new { code = target.Code, displayName = "新しいアカウント名" });
        var options = await begin.Content.ReadFromJsonAsync<JsonElement>();
        var key = RandomNumberGenerator.GetBytes(32);
        using var completed = await enrollee.PostAsJsonAsync("/auth/enrollment/verify", new { credential = app.Attestation(options, key) });
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        options = await app.BeginAsync(enrollee);
        using var login = await app.VerifyAsync(enrollee, app.AssertionFor(options, target.Id, key));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        await using (var scope = app.Factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AuthenticationRevocationService>().RevokeAccountAsync(target.Id);
        var tables = app.Database.Tables().Except(new[] { "auth_enrollment_codes" }).ToArray();
        var before = app.Database.Snapshot(tables);
        using var issued = await ReissueAsync(admin, target.Id);
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(tables));
        using var stillRejected = await enrollee.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, stillRejected.StatusCode);
        Assert.Equal(0L, app.Database.Scalar($"SELECT IsEnabled FROM AspNetUsers WHERE Id='{target.Id}'"));
        Assert.Equal(0L, app.Database.Scalar($"SELECT COUNT(*) FROM AspNetUserPasskeys WHERE UserId='{target.Id}'"));
        var code = (await issued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;
        using var recovered = app.Client();
        await LoginTestApp.CsrfAsync(recovered);
        using var recoveryBegin = await recovered.PostAsJsonAsync("/auth/enrollment/options", new { code, displayName = "新しいアカウント名" });
        Assert.Equal(HttpStatusCode.OK, recoveryBegin.StatusCode);
        options = await recoveryBegin.Content.ReadFromJsonAsync<JsonElement>();
        var newKey = RandomNumberGenerator.GetBytes(32);
        using var recoveryComplete = await recovered.PostAsJsonAsync("/auth/enrollment/verify", new { credential = app.Attestation(options, newKey) });
        Assert.Equal(HttpStatusCode.OK, recoveryComplete.StatusCode);
        using var notLoggedIn = await recovered.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, notLoggedIn.StatusCode);
        options = await app.BeginAsync(recovered);
        using var newLogin = await app.VerifyAsync(recovered, app.AssertionFor(options, target.Id, newKey));
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
        using var accounts = await recovered.GetAsync("/admin/accounts");
        Assert.Equal(targetIsAdmin ? HttpStatusCode.OK : HttpStatusCode.Forbidden, accounts.StatusCode);
        using var oldSession = await enrollee.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, oldSession.StatusCode);
    }

    [Fact]
    public async Task ExpiredUnusedCodeIsRevokedAndConsumedOrPreviouslyRevokedHistoryIsNotOverwritten()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var admin = app.Client();
        var target = await PrepareAsync(app, admin);
        app.Database.Execute($"UPDATE auth_enrollment_codes SET ExpiresAtUtc=CreatedAtUtc WHERE AuthUserId='{target.Id}'");
        app.Database.Execute($"INSERT INTO auth_enrollment_codes VALUES(X'01','{target.Id}','passkey-enrollment','2020-01-01','2020-01-02','2020-01-01',NULL)");
        app.Database.Execute($"INSERT INTO auth_enrollment_codes VALUES(X'02','{target.Id}','passkey-enrollment','2020-01-01','2020-01-02',NULL,'2020-01-01')");
        using var result = await ReissueAsync(admin, target.Id);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("2020-01-01", app.Database.Scalar("SELECT ConsumedAtUtc FROM auth_enrollment_codes WHERE CodeHash=X'01'"));
        Assert.Equal("2020-01-01", app.Database.Scalar("SELECT RevokedAtUtc FROM auth_enrollment_codes WHERE CodeHash=X'02'"));
        Assert.Equal(1L, app.Database.Scalar($"SELECT COUNT(*) FROM auth_enrollment_codes WHERE AuthUserId='{target.Id}' AND ConsumedAtUtc IS NULL AND RevokedAtUtc IS NULL"));
    }

    [Fact]
    public async Task ConcurrentReissuesLeaveOnlyTheLastIssuedCodeUsable()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var admin = app.Client();
        var target = await PrepareAsync(app, admin);
        var responses = await Task.WhenAll(ReissueAsync(admin, target.Id), ReissueAsync(admin, target.Id));
        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            var bodies = await Task.WhenAll(responses.Select(response => response.Content.ReadFromJsonAsync<JsonElement>()));
            var codes = bodies.Select(body => body.GetProperty("code").GetString()!).ToArray();
            Assert.Equal(2, codes.Distinct().Count());
            Assert.Single(codes, code => (long)app.Database.Scalar($"SELECT COUNT(*) FROM auth_enrollment_codes WHERE CodeHash=X'{Hash(code)}' AND RevokedAtUtc IS NULL")! == 1);
            Assert.Equal(1L, app.Database.Scalar($"SELECT COUNT(*) FROM auth_enrollment_codes WHERE AuthUserId='{target.Id}' AND RevokedAtUtc IS NULL AND ConsumedAtUtc IS NULL"));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task PendingEnrollmentCannotUseTheOldCodeAfterReissue()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var admin = app.Client();
        var target = await PrepareAsync(app, admin);
        using var enrollee = app.Client();
        await LoginTestApp.CsrfAsync(enrollee);
        using var begin = await enrollee.PostAsJsonAsync("/auth/enrollment/options", new { code = target.Code, displayName = "新しいアカウント名" });
        var options = await begin.Content.ReadFromJsonAsync<JsonElement>();
        using var reissued = await ReissueAsync(admin, target.Id);
        Assert.Equal(HttpStatusCode.OK, reissued.StatusCode);
        var before = app.Database.Snapshot(app.Database.Tables());
        using var completed = await enrollee.PostAsJsonAsync("/auth/enrollment/verify", new { credential = app.Attestation(options, RandomNumberGenerator.GetBytes(32)) });
        Assert.Equal(HttpStatusCode.BadRequest, completed.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task EnrollmentAndReissueAreSerializedWithoutIssuingToAnActivatedAccount()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var admin = app.Client();
        var target = await PrepareAsync(app, admin);
        using var enrollee = app.Client();
        await LoginTestApp.CsrfAsync(enrollee);
        using var begin = await enrollee.PostAsJsonAsync("/auth/enrollment/options", new { code = target.Code, displayName = "新しいアカウント名" });
        var options = await begin.Content.ReadFromJsonAsync<JsonElement>();
        var enrollment = enrollee.PostAsJsonAsync("/auth/enrollment/verify", new { credential = app.Attestation(options, RandomNumberGenerator.GetBytes(32)) });
        var reissue = ReissueAsync(admin, target.Id);
        await Task.WhenAll(enrollment, reissue);
        using var enrolled = await enrollment;
        using var reissued = await reissue;
        if (enrolled.StatusCode == HttpStatusCode.OK)
        {
            Assert.Equal(HttpStatusCode.Conflict, reissued.StatusCode);
            Assert.Equal(1L, app.Database.Scalar($"SELECT IsEnabled FROM AspNetUsers WHERE Id='{target.Id}'"));
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, enrolled.StatusCode);
            Assert.Equal(HttpStatusCode.OK, reissued.StatusCode);
            Assert.Equal(0L, app.Database.Scalar($"SELECT IsEnabled FROM AspNetUsers WHERE Id='{target.Id}'"));
        }
    }

    [Theory]
    [InlineData("missing", HttpStatusCode.NotFound)]
    [InlineData("enabled", HttpStatusCode.Conflict)]
    [InlineData("key", HttpStatusCode.Conflict)]
    [InlineData("admin-enabled", HttpStatusCode.Conflict)]
    [InlineData("admin-key", HttpStatusCode.Conflict)]
    [InlineData("self", HttpStatusCode.Conflict)]
    public async Task InvalidTargetsDoNotChangeAnything(string change, HttpStatusCode expected)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var admin = app.Client();
        var target = await PrepareAsync(app, admin);
        if (change.StartsWith("admin-", StringComparison.Ordinal) || change == "key")
        {
            await using var scope = app.Factory.Services.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AuthUser>>();
            var user = (await users.FindByIdAsync(target.Id))!;
            if (change.StartsWith("admin-", StringComparison.Ordinal)) Assert.True((await users.AddToRoleAsync(user, "Admin")).Succeeded);
            if (change is "key" or "admin-key") Assert.True((await users.AddOrUpdatePasskeyAsync(user, new UserPasskeyInfo([77], [1], app.Time.GetUtcNow(), 0, [], true, false, false, [], []))).Succeeded);
        }
        switch (change)
        {
            case "enabled":
            case "admin-enabled": app.Database.Execute($"UPDATE AspNetUsers SET IsEnabled=1 WHERE Id='{target.Id}'"); break;
        }
        var before = app.Database.Snapshot(app.Database.Tables());
        using var response = await ReissueAsync(admin, change switch { "missing" => "unknown-account", "self" => app.UserId, _ => target.Id });
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Theory]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("role", HttpStatusCode.Forbidden)]
    [InlineData("stale", HttpStatusCode.Forbidden)]
    [InlineData("future", HttpStatusCode.Forbidden)]
    [InlineData("disabled", HttpStatusCode.Unauthorized)]
    [InlineData("revoked", HttpStatusCode.Unauthorized)]
    [InlineData("expired", HttpStatusCode.Unauthorized)]
    public async Task InvalidCallerCannotReissue(string change, HttpStatusCode expected)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var admin = app.Client();
        var target = await PrepareAsync(app, admin);
        using var anonymous = app.Client();
        switch (change)
        {
            case "role": app.Database.Execute($"DELETE FROM AspNetUserRoles WHERE UserId='{app.UserId}'"); break;
            case "stale": app.Time.Advance(TimeSpan.FromMinutes(5)); break;
            case "future": app.Database.Execute("UPDATE auth_sessions SET VerifiedAtUtc=ExpiresAtUtc"); break;
            case "disabled": app.Database.Execute($"UPDATE AspNetUsers SET IsEnabled=0 WHERE Id='{app.UserId}'"); break;
            case "revoked": app.Database.Execute("UPDATE auth_sessions SET RevokedAtUtc=CreatedAtUtc"); break;
            case "expired": app.Database.Execute("UPDATE auth_sessions SET ExpiresAtUtc=CreatedAtUtc"); break;
        }
        var before = app.Database.Snapshot(app.Database.Tables());
        using var result = await ReissueAsync(change == "anonymous" ? anonymous : admin, target.Id);
        Assert.Equal(expected, result.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Theory]
    [InlineData("UPDATE")]
    [InlineData("INSERT")]
    public async Task SaveFailureRestoresTheOldCodeAndAllowsExplicitRetry(string operation)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var admin = app.Client();
        var target = await PrepareAsync(app, admin);
        app.Database.Execute($"CREATE TRIGGER reject_reissue BEFORE {operation} ON auth_enrollment_codes BEGIN SELECT RAISE(ABORT,'synthetic-private-failure'); END");
        var before = app.Database.Snapshot(app.Database.Tables());
        using var result = await ReissueAsync(admin, target.Id);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode);
        Assert.DoesNotContain("synthetic-private-failure", await result.Content.ReadAsStringAsync());
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        app.Database.Execute("DROP TRIGGER reject_reissue");
        using var retry = await ReissueAsync(admin, target.Id);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Theory]
    [InlineData("DELETE FROM AspNetUserRoles WHERE UserId='{actor}'", HttpStatusCode.Forbidden)]
    [InlineData("UPDATE AspNetUsers SET IsEnabled=0 WHERE Id='{actor}'", HttpStatusCode.Forbidden)]
    [InlineData("UPDATE auth_sessions SET RevokedAtUtc=CreatedAtUtc WHERE AuthUserId='{actor}'", HttpStatusCode.Forbidden)]
    [InlineData("UPDATE AspNetUsers SET IsEnabled=1 WHERE Id=NEW.AuthUserId", HttpStatusCode.Conflict)]
    public async Task ActorOrTargetChangesDuringSavingRollBackTheWholeReissue(string mutation, HttpStatusCode expected)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var admin = app.Client();
        var target = await PrepareAsync(app, admin);
        mutation = mutation.Replace("{actor}", app.UserId);
        app.Database.Execute($"CREATE TRIGGER change_reissue AFTER INSERT ON auth_enrollment_codes BEGIN {mutation}; END");
        var before = app.Database.Snapshot(app.Database.Tables());
        using var result = await ReissueAsync(admin, target.Id);
        Assert.Equal(expected, result.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task VerificationExpiringDuringSaveKeepsTheOldCode()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var owner = app.Client();
        var target = await PrepareAsync(app, owner);
        await using var factory = app.NewFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.ConfigureDbContext<AuthDbContext>(options => options.AddInterceptors(new DelayReissue(app.Time)))));
        using var admin = app.Client(factory);
        admin.DefaultRequestHeaders.Add("Cookie", app.SessionCookie.Split(';')[0]);
        await LoginTestApp.CsrfAsync(admin);
        var before = app.Database.Snapshot(app.Database.Tables());
        using var result = await ReissueAsync(admin, target.Id);
        Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task CsrfOriginBodyLimitAndSharedRateLimitApplyToReissue()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var admin = app.Client();
        var target = await PrepareAsync(app, admin);
        var before = app.Database.Snapshot(app.Database.Tables());
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        using var csrf = await ReissueAsync(admin, target.Id);
        Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
        await LoginTestApp.CsrfAsync(admin);
        admin.DefaultRequestHeaders.Remove("Origin");
        admin.DefaultRequestHeaders.Add("Origin", "https://other.example.test");
        using var origin = await ReissueAsync(admin, target.Id);
        Assert.Equal(HttpStatusCode.Forbidden, origin.StatusCode);
        admin.DefaultRequestHeaders.Remove("Origin");
        admin.DefaultRequestHeaders.Add("Origin", LoginTestApp.Origin);
        using var oversized = await admin.PostAsync(Path(target.Id), new StringContent(new string(' ', 65537), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        await using var isolated = app.NewFactory();
        using var limited = app.Client(isolated);
        limited.DefaultRequestHeaders.Add("Cookie", app.SessionCookie.Split(';')[0]);
        await LoginTestApp.CsrfAsync(limited);
        for (var index = 0; index < 20; index++)
        {
            using var rejected = await ReissueAsync(limited, "missing");
            Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
        }
        using var throttled = await limited.PostAsJsonAsync("/auth/login/options", new { });
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    /// <summary>管理者でログインし、既存人物の登録待ち一般アカウントを作成する。</summary>
    private static async Task<(string Id, string Code)> PrepareAsync(LoginTestApp app, HttpClient admin)
    {
        await app.LoginAsync(admin);
        await LoginTestApp.CsrfAsync(admin);
        using var created = await admin.PostAsJsonAsync("/admin/accounts", new { requestId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("authUserId").GetString()!, body.GetProperty("code").GetString()!);
    }

    /// <summary>URLに固定したアカウントへ、登録コード再発行を要求する。</summary>
    private static Task<HttpResponseMessage> ReissueAsync(HttpClient client, string id) => client.PostAsJsonAsync(Path(id), new { });

    /// <summary>認証IDをURLの単一要素として符号化する。</summary>
    private static string Path(string id) => "/admin/accounts/" + Uri.EscapeDataString(id) + "/enrollment-code";

    /// <summary>生コードをDB照合用のSHA-256ハッシュへ変換する。</summary>
    private static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    /// <summary>新コードの保存中に管理者の直近本人確認を期限切れにする。</summary>
    private sealed class DelayReissue(LoginTestApp.LoginTime time) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken token = default)
        {
            if (command.CommandText.StartsWith("INSERT INTO \"auth_enrollment_codes\"", StringComparison.Ordinal))
                time.Advance(TimeSpan.FromMinutes(5));
            return base.ReaderExecutingAsync(command, eventData, result, token);
        }
    }
}
