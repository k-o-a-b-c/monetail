using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Monetail.Api.Authentication;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>実署名・Identity・SQLite・Cookieでログインと失効の境界を検証する。</summary>
public class PasskeyLoginTests
{
    [Fact]
    public async Task LoginPersistsOnlyFixedUtcStateAndDoesNotModifyBusinessDataOrRenewCookie()
    {
        await using var app = await LoginTestApp.CreateAsync();
        var tables = app.Database.Tables().Where(name => !name.StartsWith("AspNet") &&
            !name.StartsWith("auth_") && !name.StartsWith("__EF")).ToArray();
        var before = app.Database.Snapshot(tables);
        using var client = app.Client();
        var options = await app.BeginAsync(client);
        Assert.Equal("monetail.example.test", options.GetProperty("rpId").GetString());
        Assert.Empty(options.GetProperty("allowCredentials").EnumerateArray());
        Assert.Equal("required", options.GetProperty("userVerification").GetString());
        using var response = await app.VerifyAsync(client, app.Assertion(options, count: 1));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith(SessionCookieEvents.CookieName + "=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie);
        Assert.Contains("httponly", cookie);
        Assert.Contains("samesite=lax", cookie);
        Assert.Contains("path=/", cookie);
        Assert.DoesNotContain("domain=", cookie);
        var protectedValue = cookie.Split(';')[0].Split('=', 2)[1];
        var cookieOptions = app.Factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(SessionCookieEvents.Scheme);
        var ticket = cookieOptions.TicketDataFormat.Unprotect(Uri.UnescapeDataString(protectedValue))!;
        Assert.NotNull(ticket);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(app.Time.GetUtcNow().ToUnixTimeSeconds()), ticket.Properties.IssuedUtc);
        var expected = SessionLifetime.Calculate(app.Time.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo"));
        Assert.Equal(expected, ticket.Properties.ExpiresUtc);
        Assert.False(ticket.Properties.AllowRefresh);
        Assert.False(cookieOptions.SlidingExpiration);
        Assert.DoesNotContain("Asia/Tokyo", string.Join(" ", ticket.Properties.Items.Values));
        Assert.DoesNotContain(ticket.Principal.Claims, claim => claim.Value.Contains("Asia/Tokyo", StringComparison.Ordinal));
        Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM auth_sessions"));
        Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM pragma_table_info('auth_sessions') WHERE lower(name) LIKE '%zone%'"));
        var rawId = ticket.Principal.FindFirst(SessionCookieEvents.IdClaim)!.Value;
        Assert.Equal(32, WebEncoders.Base64UrlDecode(rawId).Length);
        Assert.Equal(0L, app.Database.Scalar($"SELECT COUNT(*) FROM auth_sessions WHERE CAST(IdHash AS TEXT)='{rawId}'"));
        var sessionBefore = app.Database.Snapshot(["auth_sessions"]);
        app.Time.Advance(TimeSpan.FromDays(12));
        using var me = await client.GetAsync("/auth/me?timeZone=Europe/Berlin&userCode=2");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.False(me.Headers.Contains("Set-Cookie"));
        var person = await me.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(app.UserId, person.GetProperty("authUserId").GetString());
        Assert.Equal(1, person.GetProperty("userCode").GetInt32());
        Assert.Equal("同じ名前", person.GetProperty("displayName").GetString());
        Assert.Equal(expected, person.GetProperty("expiresAtUtc").GetDateTimeOffset());
        Assert.Equal(sessionBefore, app.Database.Snapshot(["auth_sessions"]));
        Assert.Equal(before, app.Database.Snapshot(tables));
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
    public async Task InvalidSignedResponseDoesNotCreateSessionAndConsumesOnlyChallenge(string corruption)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        var options = await app.BeginAsync(client);
        var before = app.Database.Snapshot(app.Database.Tables());
        using var response = await app.VerifyAsync(client, app.Assertion(options, corruption));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var replay = await app.VerifyAsync(client, app.Assertion(options));
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        await app.LoginAsync(client);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("key-deleted")]
    [InlineData("link-deleted")]
    [InlineData("person-deleted")]
    [InlineData("challenge-expired")]
    public async Task PendingLoginCannotUseRevokedOrMissingAuthenticationState(string change)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        var options = await app.BeginAsync(client);
        switch (change)
        {
            case "disabled": app.Database.Execute("UPDATE AspNetUsers SET IsEnabled=0"); break;
            case "key-deleted": app.Database.Execute("DELETE FROM AspNetUserPasskeys"); break;
            case "link-deleted": app.Database.Execute("DELETE FROM auth_user_links"); break;
            case "person-deleted": app.Database.Execute("DELETE FROM m_user WHERE code=1"); break;
            case "challenge-expired": app.Time.Advance(TimeSpan.FromMinutes(5)); break;
        }
        var before = app.Database.Snapshot(app.Database.Tables());
        using var response = await app.VerifyAsync(client, app.Assertion(options));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task LoginChallengeIsBrowserBoundPurposeBoundAndConsumedAtomically()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var owner = app.Client();
        var options = await app.BeginAsync(owner);
        using var other = app.Client();
        await LoginTestApp.CsrfAsync(other);
        using var stolen = await app.VerifyAsync(other, app.Assertion(options));
        Assert.Equal(HttpStatusCode.Unauthorized, stolen.StatusCode);
        using var wrongPurpose = await owner.PostAsJsonAsync("/auth/enrollment/verify", new { credential = app.Assertion(options) });
        Assert.Equal(HttpStatusCode.BadRequest, wrongPurpose.StatusCode);
        var assertion = app.Assertion(options);
        var responses = await Task.WhenAll(app.VerifyAsync(owner, assertion), app.VerifyAsync(owner, assertion));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Unauthorized);
            Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM auth_sessions"));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task RestartKeepsCommittedSessionButRejectsPendingLogin()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var loggedIn = app.Client();
        await app.LoginAsync(loggedIn);
        using var pending = app.Client();
        var options = await app.BeginAsync(pending);
        var challengeCookie = app.ChallengeCookie;
        await using var restarted = app.NewFactory();
        using var restored = app.Client(restarted);
        restored.DefaultRequestHeaders.Add("Cookie", app.SessionCookie.Split(';')[0]);
        using var me = await restored.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var unfinished = app.Client(restarted);
        unfinished.DefaultRequestHeaders.Add("Cookie", challengeCookie);
        await LoginTestApp.CsrfAsync(unfinished);
        using var verify = await app.VerifyAsync(unfinished, app.Assertion(options));
        Assert.Equal(HttpStatusCode.Unauthorized, verify.StatusCode);
        Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM auth_sessions"));
    }

    [Fact]
    public async Task LogoutRevokesOnlyCurrentSessionAndReplayedCookieCannotBeUsed()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var first = app.Client();
        using var second = app.Client();
        await app.LoginAsync(first);
        var oldCookie = app.SessionCookie.Split(';')[0];
        await app.LoginAsync(second);
        using var missingCsrf = await first.PostAsJsonAsync("/auth/logout", new { });
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        await LoginTestApp.CsrfAsync(first);
        using var logout = await first.PostAsJsonAsync("/auth/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains(logout.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith(SessionCookieEvents.CookieName + "=;", StringComparison.Ordinal));
        using var me = await first.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        using var otherMe = await second.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.OK, otherMe.StatusCode);
        using var replayClient = app.Client();
        replayClient.DefaultRequestHeaders.Add("Cookie", oldCookie);
        using var replay = await replayClient.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM auth_sessions WHERE RevokedAtUtc IS NOT NULL"));
        Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM auth_sessions WHERE RevokedAtUtc IS NULL"));
    }

    [Theory]
    [InlineData("revoked", HttpStatusCode.Unauthorized)]
    [InlineData("expired", HttpStatusCode.Unauthorized)]
    [InlineData("disabled", HttpStatusCode.Unauthorized)]
    [InlineData("person", HttpStatusCode.Forbidden)]
    [InlineData("link", HttpStatusCode.Forbidden)]
    [InlineData("store", HttpStatusCode.ServiceUnavailable)]
    public async Task EveryProtectedRequestChecksLiveDatabaseState(string change, HttpStatusCode expected)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        await app.LoginAsync(client);
        switch (change)
        {
            case "revoked": app.Database.Execute("UPDATE auth_sessions SET RevokedAtUtc=CreatedAtUtc"); break;
            case "expired": app.Database.Execute("UPDATE auth_sessions SET ExpiresAtUtc=CreatedAtUtc"); break;
            case "disabled": app.Database.Execute("UPDATE AspNetUsers SET IsEnabled=0"); break;
            case "person": app.Database.Execute("DELETE FROM m_user WHERE code=1"); break;
            case "link": app.Database.Execute("DELETE FROM auth_user_links"); break;
            case "store": app.Database.Execute("ALTER TABLE auth_sessions RENAME TO inaccessible_sessions"); break;
        }
        using var response = await client.GetAsync("/auth/me");
        Assert.Equal(expected, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task ExpiryIsEnforcedAtTheExactUtcBoundaryAndDoesNotSlide()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        await app.LoginAsync(client);
        var expiry = SessionLifetime.Calculate(app.Time.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo"));
        var before = app.Database.Snapshot(["auth_sessions"]);
        app.Time.Set(expiry.AddTicks(-1));
        using var stillValid = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.OK, stillValid.StatusCode);
        Assert.False(stillValid.Headers.Contains("Set-Cookie"));
        app.Time.Set(expiry);
        using var expired = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.Null(expired.Headers.Location);
        Assert.Equal(before, app.Database.Snapshot(["auth_sessions"]));
    }

    [Fact]
    public async Task LoginAtSixOnTheFirstWorksOnTheSeventhButExpiresBeforeSixOnTheFifteenth()
    {
        await using var app = await LoginTestApp.CreateAsync();
        var login = new DateTimeOffset(DateTime.UtcNow.Year + 1, 1, 1, 6, 0, 0, TimeSpan.FromHours(9));
        app.Time.Set(login.ToUniversalTime());
        using var client = app.Client();
        await app.LoginAsync(client, "Asia/Tokyo");
        var me = await client.GetFromJsonAsync<JsonElement>("/auth/me");
        var expectedExpiry = new DateTimeOffset(login.Year, 1, 15, 2, 0, 0, TimeSpan.FromHours(9));
        Assert.Equal(expectedExpiry.ToUniversalTime(), me.GetProperty("expiresAtUtc").GetDateTimeOffset());
        var before = app.Database.Snapshot(["auth_sessions"]);
        app.Time.Set(login.AddDays(6).ToUniversalTime());
        using var seventh = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.OK, seventh.StatusCode);
        Assert.False(seventh.Headers.Contains("Set-Cookie"));
        app.Time.Set(login.AddDays(14).ToUniversalTime());
        using var fifteenth = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, fifteenth.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(["auth_sessions"]));
    }

    [Theory]
    [InlineData("auth_sessions", "INSERT")]
    [InlineData("AspNetUserPasskeys", "UPDATE")]
    public async Task FailureRollsBackSessionAndSignatureCounterThenAllowsRetry(string table, string operation)
    {
        await using var app = await LoginTestApp.CreateAsync();
        app.Database.Execute($"CREATE TRIGGER fail_login BEFORE {operation} ON {table} BEGIN SELECT RAISE(ABORT,'injected'); END");
        var before = app.Database.Snapshot(app.Database.Tables());
        using var client = app.Client();
        var options = await app.BeginAsync(client);
        using var response = await app.VerifyAsync(client, app.Assertion(options, count: 1));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        app.Database.Execute("DROP TRIGGER fail_login");
        await app.LoginAsync(client, count: 1);
    }

    [Fact]
    public async Task PublicPagesCsrfOriginLimitsDefaultProtectionAndCurrentRolesAreEnforced()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        foreach (var path in new[] { "/health", "/auth/login", "/auth/login.js", "/auth/enroll", "/auth/csrf" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using var anonymous = await client.GetAsync("/auth/test-default");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var csrf = await client.PostAsJsonAsync("/auth/login/options", new { timeZone = "Asia/Tokyo" });
        Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
        await LoginTestApp.CsrfAsync(client);
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "https://other.example.test");
        using var origin = await client.PostAsJsonAsync("/auth/login/options", new { timeZone = "Asia/Tokyo" });
        Assert.Equal(HttpStatusCode.Forbidden, origin.StatusCode);
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", LoginTestApp.Origin);
        await app.LoginAsync(client);
        using var authorized = await client.GetAsync("/auth/test-admin");
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        app.Database.Execute("DELETE FROM AspNetUserRoles");
        using var revokedRole = await client.GetAsync("/auth/test-admin");
        Assert.Equal(HttpStatusCode.Forbidden, revokedRole.StatusCode);
        using var stillLoggedIn = await client.GetAsync("/auth/test-default");
        Assert.Equal(HttpStatusCode.OK, stillLoggedIn.StatusCode);
    }

    [Fact]
    public async Task SeparateChallengesCannotReuseTheSameIncreasingSignatureCounter()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var first = app.Client();
        using var second = app.Client();
        var firstOptions = await app.BeginAsync(first);
        var secondOptions = await app.BeginAsync(second);
        var responses = await Task.WhenAll(app.VerifyAsync(first, app.Assertion(firstOptions, count: 1)),
            app.VerifyAsync(second, app.Assertion(secondOptions, count: 1)));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Unauthorized);
            Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM auth_sessions"));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Not/A_TimeZone")]
    [InlineData("Etc/UTC")]
    public async Task NewLoginUsesItsResolvedZoneWithoutRecomputingOldSession(string? zone)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var first = app.Client();
        await app.LoginAsync(first, "Asia/Tokyo");
        var firstExpiry = app.Database.Scalar("SELECT ExpiresAtUtc FROM auth_sessions");
        using var second = app.Client();
        await app.LoginAsync(second, zone);
        var me = await second.GetFromJsonAsync<JsonElement>("/auth/me");
        var expected = SessionLifetime.Calculate(app.Time.GetUtcNow(), zone == "Etc/UTC" ? TimeZoneInfo.Utc : TimeZoneInfo.Local);
        Assert.Equal(expected, me.GetProperty("expiresAtUtc").GetDateTimeOffset());
        var oldMe = await first.GetFromJsonAsync<JsonElement>("/auth/me");
        Assert.Equal(SessionLifetime.Calculate(app.Time.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo")),
            oldMe.GetProperty("expiresAtUtc").GetDateTimeOffset());
        Assert.Equal(firstExpiry, app.Database.Scalar("SELECT ExpiresAtUtc FROM auth_sessions ORDER BY rowid LIMIT 1"));
    }

    [Fact]
    public async Task FailedLogoutKeepsSessionAndCookieUntilRevocationSucceeds()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        await app.LoginAsync(client);
        await LoginTestApp.CsrfAsync(client);
        app.Database.Execute("CREATE TRIGGER fail_logout BEFORE UPDATE ON auth_sessions BEGIN SELECT RAISE(ABORT,'injected'); END");
        using var failed = await client.PostAsJsonAsync("/auth/logout", new { });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.False(failed.Headers.Contains("Set-Cookie"));
        using var me = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        app.Database.Execute("DROP TRIGGER fail_logout");
        using var retry = await client.PostAsJsonAsync("/auth/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
    }

    [Fact]
    public async Task LoginAttemptsShareTheBoundedRateLimitAndTamperedCookiesAreRejected()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        client.DefaultRequestHeaders.Add("Cookie", SessionCookieEvents.CookieName + "=tampered");
        using var invalid = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        await LoginTestApp.CsrfAsync(client);
        for (var index = 0; index < 20; index++)
        {
            using var response = await client.PostAsJsonAsync("/auth/login/options", new { timeZone = "Etc/UTC" });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using var limited = await client.PostAsJsonAsync("/auth/login/options", new { timeZone = "Etc/UTC" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM auth_sessions"));
    }
}
