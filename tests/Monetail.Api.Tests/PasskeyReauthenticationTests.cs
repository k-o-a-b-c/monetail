using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Authentication;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>実署名・Identity・SQLiteで、同じ本人とセッションだけを再確認する。</summary>
public class PasskeyReauthenticationTests
{
    [Fact]
    public async Task ReauthenticationUpdatesOnlyCurrentVerificationWithoutRenewingCookieOrExpiry()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var first = app.Client();
        using var second = app.Client();
        await app.LoginAsync(first);
        var originalCookie = app.SessionCookie.Split(';')[0];
        await app.LoginAsync(second);
        var tables = app.Database.Tables().Where(name => name != "auth_sessions" && name != "AspNetUserPasskeys" && name != "AspNetUsers").ToArray();
        var before = app.Database.Snapshot(tables);
        var accountsBefore = await AccountSnapshotAsync(app);
        var original = await first.GetFromJsonAsync<JsonElement>("/auth/me");
        app.Time.Advance(TimeSpan.FromDays(12));
        using var requiresConfirmation = await first.GetAsync("/auth/test-recent");
        Assert.Equal(HttpStatusCode.Forbidden, requiresConfirmation.StatusCode);
        var options = await BeginAsync(first);
        Assert.Equal("required", options.GetProperty("userVerification").GetString());
        var allowed = options.GetProperty("allowCredentials").EnumerateArray().ToArray();
        Assert.Single(allowed);
        Assert.Equal(WebEncoders.Base64UrlEncode(app.CredentialId), allowed[0].GetProperty("id").GetString());
        using var response = await VerifyAsync(first, app.Assertion(options, count: 1));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith(SessionCookieEvents.CookieName + "=", StringComparison.Ordinal));
        var me = await first.GetFromJsonAsync<JsonElement>("/auth/me");
        Assert.Equal(original.GetProperty("authUserId").GetString(), me.GetProperty("authUserId").GetString());
        Assert.Equal(original.GetProperty("expiresAtUtc").GetDateTimeOffset(), me.GetProperty("expiresAtUtc").GetDateTimeOffset());
        Assert.Equal(app.Time.GetUtcNow(), me.GetProperty("verifiedAtUtc").GetDateTimeOffset());
        Assert.Equal(2L, app.Database.Scalar("SELECT COUNT(*) FROM auth_sessions"));
        var other = await second.GetFromJsonAsync<JsonElement>("/auth/me");
        Assert.Equal(original.GetProperty("verifiedAtUtc").GetDateTimeOffset(), other.GetProperty("verifiedAtUtc").GetDateTimeOffset());
        using var recent = await first.GetAsync("/auth/test-recent-admin");
        Assert.Equal(HttpStatusCode.OK, recent.StatusCode);
        using var otherRecent = await second.GetAsync("/auth/test-recent");
        Assert.Equal(HttpStatusCode.Forbidden, otherRecent.StatusCode);
        using var restored = app.Client();
        restored.DefaultRequestHeaders.Add("Cookie", originalCookie);
        using var originalStillWorks = await restored.GetAsync("/auth/test-recent");
        Assert.Equal(HttpStatusCode.OK, originalStillWorks.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(tables));
        Assert.Equal(accountsBefore, await AccountSnapshotAsync(app));
        app.Time.Set(me.GetProperty("expiresAtUtc").GetDateTimeOffset());
        using var expired = await first.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    [Fact]
    public async Task FiveMinuteBoundaryDoesNotExtendOnUseAndFutureVerificationIsRejected()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        await app.LoginAsync(client);
        var verified = app.Time.GetUtcNow();
        using var justLoggedIn = await client.GetAsync("/auth/test-recent");
        Assert.Equal(HttpStatusCode.OK, justLoggedIn.StatusCode);
        app.Time.Set(verified.AddMinutes(5).AddTicks(-1));
        using var before = await client.GetAsync("/auth/test-recent");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        app.Time.Set(verified.AddMinutes(5));
        using var exact = await client.GetAsync("/auth/test-recent");
        Assert.Equal(HttpStatusCode.Forbidden, exact.StatusCode);
        Assert.False(exact.Headers.Contains("Set-Cookie"));
        using var normal = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.OK, normal.StatusCode);
        app.Time.Set(verified.AddTicks(-1));
        using var future = await client.GetAsync("/auth/test-recent");
        Assert.Equal(HttpStatusCode.Forbidden, future.StatusCode);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("origin")]
    [InlineData("challenge")]
    [InlineData("rp")]
    [InlineData("uv")]
    [InlineData("cross-origin")]
    [InlineData("credential")]
    [InlineData("user")]
    public async Task InvalidAssertionDoesNotRefreshVerificationAndConsumesChallenge(string corruption)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        await app.LoginAsync(client);
        app.Time.Advance(TimeSpan.FromMinutes(6));
        var options = await BeginAsync(client);
        var before = app.Database.Snapshot(app.Database.Tables());
        using var invalid = await VerifyAsync(client, app.Assertion(options, corruption));
        Assert.Equal(HttpStatusCode.Forbidden, invalid.StatusCode);
        using var replay = await VerifyAsync(client, app.Assertion(options));
        Assert.Equal(HttpStatusCode.Forbidden, replay.StatusCode);
        using var stale = await client.GetAsync("/auth/test-recent");
        Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task AnotherAccountsValidPasskeyCannotConfirmCurrentPersonOrChangeIdentity()
    {
        await using var app = await LoginTestApp.CreateAsync();
        var other = await app.AddAccountAsync(2);
        using var client = app.Client();
        await app.LoginAsync(client);
        app.Time.Advance(TimeSpan.FromMinutes(6));
        var options = await BeginAsync(client);
        var before = app.Database.Snapshot(app.Database.Tables());
        using var rejected = await VerifyAsync(client, app.AssertionFor(options, other.UserId, other.CredentialId));
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        var me = await client.GetFromJsonAsync<JsonElement>("/auth/me");
        Assert.Equal(app.UserId, me.GetProperty("authUserId").GetString());
        using var stale = await client.GetAsync("/auth/test-recent");
        Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
    }

    [Theory]
    [InlineData("disabled", HttpStatusCode.Unauthorized)]
    [InlineData("revoked", HttpStatusCode.Unauthorized)]
    [InlineData("expired", HttpStatusCode.Unauthorized)]
    [InlineData("key", HttpStatusCode.Forbidden)]
    [InlineData("challenge", HttpStatusCode.Forbidden)]
    [InlineData("store", HttpStatusCode.ServiceUnavailable)]
    public async Task PendingConfirmationRechecksCurrentAuthenticationState(string change, HttpStatusCode expected)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        await app.LoginAsync(client);
        var options = await BeginAsync(client);
        switch (change)
        {
            case "disabled": app.Database.Execute("UPDATE AspNetUsers SET IsEnabled=0"); break;
            case "revoked": app.Database.Execute("UPDATE auth_sessions SET RevokedAtUtc=CreatedAtUtc"); break;
            case "expired": app.Database.Execute("UPDATE auth_sessions SET ExpiresAtUtc=CreatedAtUtc"); break;
            case "key": app.Database.Execute("DELETE FROM AspNetUserPasskeys"); break;
            case "challenge": app.Time.Advance(TimeSpan.FromMinutes(5)); break;
            case "store": app.Database.Execute("ALTER TABLE auth_sessions RENAME TO inaccessible_sessions"); break;
        }
        var before = app.Database.Snapshot(app.Database.Tables());
        using var rejected = await VerifyAsync(client, app.Assertion(options, count: 1));
        Assert.Equal(expected, rejected.StatusCode);
        Assert.Null(rejected.Headers.Location);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task ChallengeIsBoundToCurrentSessionEvenWhenBothSessionsBelongToSamePerson()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var first = app.Client();
        using var second = app.Client();
        await app.LoginAsync(first);
        await app.LoginAsync(second);
        var secondCookie = app.SessionCookie.Split(';')[0];
        await LoginTestApp.CsrfAsync(first);
        using var begin = await first.PostAsJsonAsync("/auth/reauthentication/options", new { });
        Assert.Equal(HttpStatusCode.OK, begin.StatusCode);
        var options = await begin.Content.ReadFromJsonAsync<JsonElement>();
        var challengeCookie = begin.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        await LoginTestApp.CsrfAsync(second);
        using var wrongBrowser = await VerifyAsync(second, app.Assertion(options));
        Assert.Equal(HttpStatusCode.Forbidden, wrongBrowser.StatusCode);
        using var copied = app.Client();
        copied.DefaultRequestHeaders.Add("Cookie", secondCookie + "; " + challengeCookie);
        await LoginTestApp.CsrfAsync(copied);
        var before = app.Database.Snapshot(app.Database.Tables());
        using var wrongSession = await VerifyAsync(copied, app.Assertion(options));
        Assert.Equal(HttpStatusCode.Forbidden, wrongSession.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task LoginAndReauthenticationChallengesCannotBeUsedForEachOthersPurpose()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        await app.LoginAsync(client);
        var reauthentication = await BeginAsync(client);
        var login = await app.BeginAsync(client);
        var before = app.Database.Snapshot(app.Database.Tables());
        using var wrongLogin = await app.VerifyAsync(client, app.Assertion(reauthentication));
        Assert.Equal(HttpStatusCode.Unauthorized, wrongLogin.StatusCode);
        using var wrongReauthentication = await VerifyAsync(client, app.Assertion(login));
        Assert.Equal(HttpStatusCode.Forbidden, wrongReauthentication.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task ConfirmationIsSingleUseAndSharedCounterCannotBeReusedAcrossChallenges()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        await app.LoginAsync(client);
        app.Time.Advance(TimeSpan.FromMinutes(6));
        var options = await BeginAsync(client);
        var credential = app.Assertion(options, count: 1);
        var responses = await Task.WhenAll(VerifyAsync(client, credential), VerifyAsync(client, credential));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Forbidden);
        }
        finally { foreach (var response in responses) response.Dispose(); }
        using var other = app.Client();
        await app.LoginAsync(other, count: 2);
        var firstOptions = await BeginAsync(client);
        var otherOptions = await BeginAsync(other);
        responses = await Task.WhenAll(VerifyAsync(client, app.Assertion(firstOptions, count: 3)),
            VerifyAsync(other, app.Assertion(otherOptions, count: 3)));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Forbidden);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Theory]
    [InlineData("key")]
    [InlineData("verification")]
    [InlineData("revoked-during-save")]
    public async Task FailureRollsBackBothCounterAndVerificationThenAllowsNewAttempt(string failure)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        await app.LoginAsync(client);
        app.Time.Advance(TimeSpan.FromMinutes(6));
        var trigger = failure switch
        {
            "key" => "BEFORE UPDATE ON AspNetUserPasskeys BEGIN SELECT RAISE(ABORT,'injected'); END",
            "verification" => "BEFORE UPDATE ON auth_sessions BEGIN SELECT RAISE(ABORT,'injected'); END",
            _ => "AFTER UPDATE ON AspNetUserPasskeys BEGIN UPDATE auth_sessions SET RevokedAtUtc=CreatedAtUtc; END"
        };
        app.Database.Execute("CREATE TRIGGER fail_reauthentication " + trigger);
        var before = app.Database.Snapshot(app.Database.Tables());
        var options = await BeginAsync(client);
        using var failed = await VerifyAsync(client, app.Assertion(options, count: 1));
        Assert.Equal(failure == "revoked-during-save" ? HttpStatusCode.Forbidden : HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        app.Database.Execute("DROP TRIGGER fail_reauthentication");
        var retryOptions = await BeginAsync(client);
        using var retry = await VerifyAsync(client, app.Assertion(retryOptions, count: 1));
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Fact]
    public async Task RestartPreservesConfirmedSessionButRejectsUnfinishedChallenge()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        await app.LoginAsync(client);
        app.Time.Advance(TimeSpan.FromMinutes(6));
        var confirmedOptions = await BeginAsync(client);
        using var confirmed = await VerifyAsync(client, app.Assertion(confirmedOptions));
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        using var begin = await client.PostAsJsonAsync("/auth/reauthentication/options", new { });
        var options = await begin.Content.ReadFromJsonAsync<JsonElement>();
        var challengeCookie = begin.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        await using var restarted = app.NewFactory();
        using var restored = app.Client(restarted);
        restored.DefaultRequestHeaders.Add("Cookie", app.SessionCookie.Split(';')[0] + "; " + challengeCookie);
        await LoginTestApp.CsrfAsync(restored);
        using var recent = await restored.GetAsync("/auth/test-recent");
        Assert.Equal(HttpStatusCode.OK, recent.StatusCode);
        var before = app.Database.Snapshot(app.Database.Tables());
        using var rejected = await VerifyAsync(restored, app.Assertion(options));
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task ReauthenticationRequiresSessionCsrfOriginAndSharesLoginRateLimit()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var anonymous = app.Client();
        await LoginTestApp.CsrfAsync(anonymous);
        foreach (var path in new[] { "/auth/reauthentication/options", "/auth/reauthentication/verify" })
        {
            using var denied = await anonymous.PostAsJsonAsync(path, new { credential = new { } });
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        }
        using var client = app.Client();
        await app.LoginAsync(client);
        using var staleCsrf = await client.PostAsJsonAsync("/auth/reauthentication/options", new { });
        Assert.Equal(HttpStatusCode.BadRequest, staleCsrf.StatusCode);
        await LoginTestApp.CsrfAsync(client);
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "https://other.example.test");
        using var wrongOrigin = await client.PostAsJsonAsync("/auth/reauthentication/options", new { });
        Assert.Equal(HttpStatusCode.Forbidden, wrongOrigin.StatusCode);
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", LoginTestApp.Origin);
        await using var isolated = app.NewFactory();
        using var bounded = app.Client(isolated);
        bounded.DefaultRequestHeaders.Add("Cookie", app.SessionCookie.Split(';')[0]);
        await LoginTestApp.CsrfAsync(bounded);
        for (var index = 0; index < 10; index++)
        {
            using var login = await bounded.PostAsJsonAsync("/auth/login/options", new { });
            using var confirmation = await bounded.PostAsJsonAsync("/auth/reauthentication/options", new { });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            Assert.Equal(HttpStatusCode.OK, confirmation.StatusCode);
        }
        using var limited = await bounded.PostAsJsonAsync("/auth/reauthentication/options", new { });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task FreshVerificationDoesNotGrantAdminRoleOrPreserveRevokedRole()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        await app.LoginAsync(client);
        using var admin = await client.GetAsync("/auth/test-recent-admin");
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        app.Database.Execute("DELETE FROM AspNetUserRoles");
        using var revoked = await client.GetAsync("/auth/test-recent-admin");
        Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        var options = await BeginAsync(client);
        using var confirmed = await VerifyAsync(client, app.Assertion(options));
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        using var stillNotAdmin = await client.GetAsync("/auth/test-recent-admin");
        Assert.Equal(HttpStatusCode.Forbidden, stillNotAdmin.StatusCode);
        using var allowed = await client.GetAsync("/auth/test-recent");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpiryDuringSignatureVerificationRollsBackCounterAndConfirmation(bool sessionExpires)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        await app.LoginAsync(client);
        var me = await client.GetFromJsonAsync<JsonElement>("/auth/me");
        var expiry = me.GetProperty("expiresAtUtc").GetDateTimeOffset();
        if (sessionExpires) app.Time.Set(expiry.AddMinutes(-1));
        await using var delayed = app.NewFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddScoped<PasskeyHandler<AuthUser>>();
            services.AddScoped<IPasskeyHandler<AuthUser>>(provider => new DelayedAssertionHandler(
                provider.GetRequiredService<PasskeyHandler<AuthUser>>(), () =>
                {
                    if (sessionExpires) app.Time.Set(expiry);
                    else app.Time.Advance(TimeSpan.FromMinutes(5));
                }));
        }));
        using var current = app.Client(delayed);
        current.DefaultRequestHeaders.Add("Cookie", app.SessionCookie.Split(';')[0]);
        var options = await BeginAsync(current);
        var before = app.Database.Snapshot(app.Database.Tables());
        using var rejected = await VerifyAsync(current, app.Assertion(options, count: 1));
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    /// <summary>現在の本人に対応するCSRFを取得し、再確認専用オプションを受け取る。</summary>
    private static async Task<JsonElement> BeginAsync(HttpClient client)
    {
        await LoginTestApp.CsrfAsync(client);
        using var response = await client.PostAsJsonAsync("/auth/reauthentication/options", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>署名応答を本人再確認専用の受付へ送信する。</summary>
    private static Task<HttpResponseMessage> VerifyAsync(HttpClient client, object credential) =>
        client.PostAsJsonAsync("/auth/reauthentication/verify", new { credential });

    /// <summary>標準Identityが更新する競合検出値を除き、アカウントの全保存プロパティを比較する。</summary>
    private static async Task<string> AccountSnapshotAsync(LoginTestApp app)
    {
        await using var scope = app.Factory.Services.CreateAsyncScope();
        var users = await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Users.AsNoTracking().OrderBy(user => user.Id).ToListAsync();
        foreach (var user in users) user.ConcurrencyStamp = null;
        return JsonSerializer.Serialize(users);
    }

    /// <summary>標準の署名検証を維持し、検証中にサーバー時刻が期限へ到達する状況を作る。</summary>
    private sealed class DelayedAssertionHandler(IPasskeyHandler<AuthUser> inner, Action advance) : IPasskeyHandler<AuthUser>
    {
        public Task<PasskeyCreationOptionsResult> MakeCreationOptionsAsync(PasskeyUserEntity userEntity, HttpContext http) =>
            inner.MakeCreationOptionsAsync(userEntity, http);
        public Task<PasskeyRequestOptionsResult> MakeRequestOptionsAsync(AuthUser? user, HttpContext http) =>
            inner.MakeRequestOptionsAsync(user, http);
        public Task<PasskeyAttestationResult> PerformAttestationAsync(PasskeyAttestationContext context) =>
            inner.PerformAttestationAsync(context);
        public async Task<PasskeyAssertionResult<AuthUser>> PerformAssertionAsync(PasskeyAssertionContext context)
        {
            var result = await inner.PerformAssertionAsync(context);
            advance();
            return result;
        }
    }
}
