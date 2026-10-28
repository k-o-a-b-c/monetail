using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Authentication;
using OpenIddict.Abstractions;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>実パスキー署名と標準OAuth処理を通して、期限・失効・PKCEを検証する。</summary>
public class DesktopOAuthTests
{
    private const string Callback = "http://127.0.0.1:49152/callback/";

    [Fact]
    public async Task DiscoveryRequiresS256AndPublishesOnlyStandardCodeFlow()
    {
        await using var app = await LoginTestApp.CreateAsync(desktopOAuth: true);
        using var native = Native(app);
        var discovery = await native.GetFromJsonAsync<JsonElement>("/.well-known/openid-configuration");
        Assert.Equal(LoginTestApp.Origin + "/", discovery.GetProperty("issuer").GetString());
        Assert.Equal(new[] { "S256" }, discovery.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(x => x.GetString()!).ToArray());
        Assert.Equal(new[] { "query" }, discovery.GetProperty("response_modes_supported").EnumerateArray().Select(x => x.GetString()!).ToArray());
        Assert.DoesNotContain("openid", discovery.GetProperty("scopes_supported").EnumerateArray().Select(x => x.GetString()));
        Assert.DoesNotContain("password", discovery.GetProperty("grant_types_supported").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task ExistingBrowserLoginIssuesIndependentNativeSessionWithSameDeadlineAndNoNewCookie()
    {
        await using var app = await LoginTestApp.CreateAsync(desktopOAuth: true);
        using var browser = app.Client();
        await app.LoginAsync(browser);
        var original = await browser.GetFromJsonAsync<JsonElement>("/auth/me");
        app.Time.Advance(TimeSpan.FromDays(12));
        using var native = Native(app);
        var tokens = await IssueAsync(app, browser, native);
        Assert.Equal("Bearer", tokens.GetProperty("token_type").GetString());
        Assert.InRange(tokens.GetProperty("expires_in").GetInt32(), 899, 900);
        Assert.True(tokens.TryGetProperty("refresh_token", out _));
        using var me = await native.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.False(me.Headers.Contains("Set-Cookie"));
        var current = await me.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(app.UserId, current.GetProperty("authUserId").GetString());
        Assert.Equal(original.GetProperty("expiresAtUtc").GetString(), current.GetProperty("expiresAtUtc").GetString());
        Assert.Equal(original.GetProperty("verifiedAtUtc").GetString(), current.GetProperty("verifiedAtUtc").GetString());
        Assert.Equal(2L, app.Database.Scalar("SELECT COUNT(*) FROM auth_sessions"));
        Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM auth_sessions WHERE RevokedAtUtc IS NOT NULL"));
        using var create = await native.PostAsJsonAsync("/ledgers", new { name = "Windowsからの家計簿", requestId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var list = await native.GetFromJsonAsync<JsonElement>("/ledgers");
        Assert.Single(list.EnumerateArray());
        Assert.Equal("Windowsからの家計簿", list[0].GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("plain")]
    [InlineData("redirect")]
    [InlineData("client")]
    [InlineData("scope")]
    public async Task InvalidAuthorizationRequestsDoNotCreateSessions(string change)
    {
        await using var app = await LoginTestApp.CreateAsync(desktopOAuth: true);
        using var browser = app.Client();
        await app.LoginAsync(browser);
        var values = Parameters(Verifier());
        switch (change)
        {
            case "missing": values.Remove("code_challenge"); break;
            case "plain": values["code_challenge_method"] = "plain"; break;
            case "redirect": values["redirect_uri"] = "https://untrusted.example.test/callback/"; break;
            case "client": values["client_id"] = "unregistered"; break;
            case "scope": values["scope"] = "offline_access"; break;
        }
        var response = await browser.GetAsync(QueryHelpers.AddQueryString("/connect/authorize", values));
        for (var attempt = 0; attempt < 3 && response.StatusCode == HttpStatusCode.Redirect &&
             response.Headers.Location is { } next &&
             new Uri(browser.BaseAddress!, next).Host == browser.BaseAddress!.Host; attempt++)
        {
            response.Dispose();
            response = await browser.GetAsync(next);
        }
        using (response)
        {
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.Redirect);
            Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM auth_sessions"));
            if (response.Headers.Location is { } target)
            {
                Assert.Equal("127.0.0.1", target.Host);
                var query = QueryHelpers.ParseQuery(target.Query);
                Assert.False(query.ContainsKey("code"));
                Assert.True(query.ContainsKey("error"));
            }
        }
    }

    [Fact]
    public async Task AnonymousAuthorizationReturnsToTheCachedRequestAfterLogin()
    {
        await using var app = await LoginTestApp.CreateAsync(desktopOAuth: true);
        using var browser = app.Client();
        using var native = Native(app);
        var verifier = Verifier();
        using var first = await browser.GetAsync(QueryHelpers.AddQueryString("/connect/authorize", Parameters(verifier)));
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        var next = first.Headers.Location!;
        if ((next.IsAbsoluteUri ? next.AbsolutePath : next.OriginalString.Split('?')[0]) != "/auth/login")
        {
            using var second = await browser.GetAsync(next);
            next = second.Headers.Location!;
        }
        Assert.Equal("/auth/login", next.IsAbsoluteUri ? next.AbsolutePath : next.OriginalString.Split('?')[0]);
        var query = QueryHelpers.ParseQuery(next.IsAbsoluteUri ? next.Query : next.OriginalString[(next.OriginalString.IndexOf('?'))..]);
        var target = query["returnUrl"].ToString();
        Assert.StartsWith("/connect/authorize?", target);
        await app.LoginAsync(browser);
        var code = await CodeFromAsync(browser, target);
        using var exchange = await ExchangeAsync(native, code, verifier);
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
    }

    [Fact]
    public async Task WrongVerifierAndCodeReplayAreRejectedByStandardOAuthValidation()
    {
        await using var app = await LoginTestApp.CreateAsync(desktopOAuth: true);
        using var browser = app.Client();
        await app.LoginAsync(browser);
        using var native = Native(app);
        var verifier = Verifier();
        var code = await CodeFromAsync(browser, QueryHelpers.AddQueryString("/connect/authorize", Parameters(verifier)));
        using var wrong = await ExchangeAsync(native, code, Verifier());
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        using var correct = await ExchangeAsync(native, code, verifier);
        Assert.Equal(HttpStatusCode.OK, correct.StatusCode);
        using var replay = await ExchangeAsync(native, code, verifier);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("session")]
    [InlineData("expired")]
    public async Task RevokedOrExpiredSessionsRejectAccessAndRefreshImmediately(string change)
    {
        await using var app = await LoginTestApp.CreateAsync(desktopOAuth: true);
        using var browser = app.Client();
        await app.LoginAsync(browser);
        using var native = Native(app);
        var tokens = await IssueAsync(app, browser, native);
        switch (change)
        {
            case "disabled": app.Database.Execute("UPDATE AspNetUsers SET IsEnabled=0"); break;
            case "session": app.Database.Execute("UPDATE auth_sessions SET RevokedAtUtc='2026-10-28'"); break;
            case "expired": app.Database.Execute("UPDATE auth_sessions SET ExpiresAtUtc='2000-01-01'"); break;
        }
        using var read = await native.GetAsync("/ledgers");
        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        using var refresh = await RefreshAsync(native, tokens.GetProperty("refresh_token").GetString()!);
        Assert.Equal(HttpStatusCode.BadRequest, refresh.StatusCode);
    }

    [Fact]
    public async Task TokenRefreshAndServerRestartKeepTheOriginalFixedDeadline()
    {
        await using var app = await LoginTestApp.CreateAsync(desktopOAuth: true);
        using var browser = app.Client();
        await app.LoginAsync(browser);
        var original = await browser.GetFromJsonAsync<JsonElement>("/auth/me");
        using var native = Native(app);
        var tokens = await IssueAsync(app, browser, native);
        app.Time.Advance(TimeSpan.FromMinutes(16));
        using var expired = await native.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        await using var restarted = app.NewFactory();
        using var next = Native(app, restarted);
        using var refresh = await RefreshAsync(next, tokens.GetProperty("refresh_token").GetString()!);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var renewed = await refresh.Content.ReadFromJsonAsync<JsonElement>();
        next.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", renewed.GetProperty("access_token").GetString());
        var current = await next.GetFromJsonAsync<JsonElement>("/auth/me");
        Assert.Equal(original.GetProperty("expiresAtUtc").GetString(), current.GetProperty("expiresAtUtc").GetString());
        Assert.Equal(original.GetProperty("verifiedAtUtc").GetString(), current.GetProperty("verifiedAtUtc").GetString());
        Assert.Equal(2L, app.Database.Scalar("SELECT COUNT(*) FROM auth_sessions"));
    }

    [Fact]
    public async Task NativeLogoutRevokesOnlyItsOwnSessionAndDoesNotSignOutBrowser()
    {
        await using var app = await LoginTestApp.CreateAsync(desktopOAuth: true);
        using var browser = app.Client();
        await app.LoginAsync(browser);
        using var native = Native(app);
        var tokens = await IssueAsync(app, browser, native);
        using var logout = await native.PostAsync("/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var nativeRead = await native.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, nativeRead.StatusCode);
        using var browserRead = await browser.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.OK, browserRead.StatusCode);
        using var refresh = await RefreshAsync(native, tokens.GetProperty("refresh_token").GetString()!);
        Assert.Equal(HttpStatusCode.BadRequest, refresh.StatusCode);
    }

    [Fact]
    public async Task InvalidBearerNeverFallsBackToValidBrowserCookie()
    {
        await using var app = await LoginTestApp.CreateAsync(desktopOAuth: true);
        using var browser = app.Client();
        await app.LoginAsync(browser);
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid");
        using var rejected = await browser.GetAsync("/ledgers");
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        browser.DefaultRequestHeaders.Authorization = null;
        using var accepted = await browser.GetAsync("/ledgers");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [Fact]
    public async Task InvalidOAuthRequestsAndPasskeyRequestsShareOneIpQuota()
    {
        await using var app = await LoginTestApp.CreateAsync(desktopOAuth: true);
        using var browser = app.Client();
        for (var index = 0; index < 20; index++)
        {
            using var invalid = await browser.GetAsync("/connect/authorize?client_id=unknown");
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        await LoginTestApp.CsrfAsync(browser);
        using var limited = await browser.PostAsJsonAsync("/auth/login/options", new { });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task CookieAndNativeSessionsShareTheSameAccountCreationQuota()
    {
        await using var app = await LoginTestApp.CreateAsync(desktopOAuth: true);
        using var browser = app.Client();
        await app.LoginAsync(browser);
        using var native = Native(app);
        await IssueAsync(app, browser, native);
        await LoginTestApp.CsrfAsync(browser);
        for (var index = 0; index < 20; index++)
        {
            using var invalid = await browser.PostAsJsonAsync("/ledgers", new { name = "", requestId = Guid.NewGuid() });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using var limited = await native.PostAsJsonAsync("/ledgers", new { name = "", requestId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        using var read = await native.GetAsync("/ledgers");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Fact]
    public async Task AuthorizationCodeExpiresWithoutCreatingUsableTokens()
    {
        await using var app = await LoginTestApp.CreateAsync(desktopOAuth: true);
        using var browser = app.Client();
        await app.LoginAsync(browser);
        using var native = Native(app);
        var verifier = Verifier();
        var code = await CodeFromAsync(browser, QueryHelpers.AddQueryString("/connect/authorize", Parameters(verifier)));
        app.Time.Advance(TimeSpan.FromMinutes(2));
        using var response = await ExchangeAsync(native, code, verifier);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(code, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task LatestRolesAndOAuthTokenRevocationAreCheckedOnEveryRequest()
    {
        await using var app = await LoginTestApp.CreateAsync(desktopOAuth: true);
        using var browser = app.Client();
        await app.LoginAsync(browser);
        using var native = Native(app);
        await IssueAsync(app, browser, native);
        using var before = await native.GetAsync("/admin/accounts");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        app.Database.Execute("DELETE FROM AspNetUserRoles");
        using var after = await native.GetAsync("/admin/accounts");
        Assert.Equal(HttpStatusCode.Forbidden, after.StatusCode);
        await using var scope = app.Factory.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        var revokedCount = 0;
        var types = new List<string?>();
        await foreach (var token in manager.FindBySubjectAsync(app.UserId))
        {
            var type = await manager.GetTypeAsync(token);
            types.Add(type);
            if (type == OpenIddictConstants.TokenTypeIdentifiers.AccessToken)
            {
                Assert.True(await manager.TryRevokeAsync(token));
                revokedCount++;
            }
        }
        Assert.True(revokedCount > 0, string.Join(",", types));
        using var revoked = await native.GetAsync("/ledgers");
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }

    [Fact]
    public async Task MissingSessionStoreReturns503WithoutRecreatingTablesOrReturningCredentials()
    {
        await using var app = await LoginTestApp.CreateAsync(desktopOAuth: true);
        using var browser = app.Client();
        await app.LoginAsync(browser);
        using var native = Native(app);
        var tokens = await IssueAsync(app, browser, native);
        app.Database.Execute("DROP TABLE auth_sessions");
        using var response = await native.GetAsync("/ledgers");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("auth_sessions", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(tokens.GetProperty("access_token").GetString()!, await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain("auth_sessions", app.Database.Tables());
    }

    /// <summary>ブラウザーのCookie・Origin・CSRFを持たないWindows版相当のクライアントを作る。</summary>
    private static HttpClient Native(LoginTestApp app, WebApplicationFactory<Program>? factory = null) =>
        (factory ?? app.Factory).CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(LoginTestApp.Origin), AllowAutoRedirect = false, HandleCookies = false
        });

    /// <summary>PKCEの秘密値を十分な乱数から生成する。</summary>
    private static string Verifier() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    /// <summary>公式公開クライアント・S256・ループバックの認可要求を構成する。</summary>
    private static Dictionary<string, string?> Parameters(string verifier) => new()
    {
        ["client_id"] = DesktopOAuthModel.ClientId, ["redirect_uri"] = Callback,
        ["response_type"] = "code", ["scope"] = DesktopOAuthModel.Scope + " offline_access",
        ["code_challenge"] = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
        ["code_challenge_method"] = "S256", ["state"] = "synthetic-state"
    };

    /// <summary>同じHTTPSホスト内の認可処理をたどり、ループバックへ返されたコードを取得する。</summary>
    private static async Task<string> CodeFromAsync(HttpClient browser, string target)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var response = await browser.GetAsync(target);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            var location = response.Headers.Location!;
            if (location.IsAbsoluteUri && location.Host == "127.0.0.1")
            {
                var query = QueryHelpers.ParseQuery(location.Query);
                Assert.False(query.ContainsKey("error"), query.TryGetValue("error_description", out var description) ? description.ToString() : "OAuth error");
                Assert.Equal("synthetic-state", query["state"].ToString());
                return query["code"].ToString();
            }
            target = location.ToString();
        }
        throw new InvalidOperationException("認可コードを取得できませんでした。");
    }

    /// <summary>Cookieではなく標準の認可コードとPKCEを使ってトークンを受け取る。</summary>
    private static Task<HttpResponseMessage> ExchangeAsync(HttpClient native, string code, string verifier) =>
        native.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["client_id"] = DesktopOAuthModel.ClientId,
            ["redirect_uri"] = Callback, ["code"] = code, ["code_verifier"] = verifier
        }));

    /// <summary>固定期限内で更新トークンを交換する。</summary>
    private static Task<HttpResponseMessage> RefreshAsync(HttpClient native, string refresh) =>
        native.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token", ["client_id"] = DesktopOAuthModel.ClientId, ["refresh_token"] = refresh
        }));

    /// <summary>ブラウザー認証からWindows版相当クライアントへAPI利用権限を引き継ぐ。</summary>
    private static async Task<JsonElement> IssueAsync(LoginTestApp app, HttpClient browser, HttpClient native)
    {
        var verifier = Verifier();
        var code = await CodeFromAsync(browser, QueryHelpers.AddQueryString("/connect/authorize", Parameters(verifier)));
        using var response = await ExchangeAsync(native, code, verifier);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var tokens = await response.Content.ReadFromJsonAsync<JsonElement>();
        native.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("access_token").GetString());
        return tokens;
    }
}
