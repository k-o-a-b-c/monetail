using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Authentication;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>本人の鍵一覧・追加・削除と、最後の鍵保護・全端末失効を実際の認証経路で検証する。</summary>
public class PasskeyManagementTests
{
    [Fact]
    public async Task ListContainsOnlyOwnPublicMetadataAndDoesNotRequireRecentVerificationOrCsrf()
    {
        await using var app = await LoginTestApp.CreateAsync();
        var other = await app.AddAccountAsync(2);
        using var anonymous = app.Client();
        using var denied = await anonymous.GetAsync("/auth/passkeys");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var client = app.Client();
        await app.LoginAsync(client);
        app.Time.Advance(TimeSpan.FromMinutes(6));
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        var before = app.Database.Snapshot(app.Database.Tables());
        var list = await client.GetFromJsonAsync<JsonElement>("/auth/passkeys");
        var row = Assert.Single(list.EnumerateArray());
        Assert.Equal(WebEncoders.Base64UrlEncode(app.CredentialId), row.GetProperty("id").GetString());
        Assert.Equal(new[] { "createdAtUtc", "id", "name" }, row.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal("synthetic-login-key", row.GetProperty("name").GetString());
        Assert.DoesNotContain(WebEncoders.Base64UrlEncode(other.CredentialId), list.GetRawText());
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task AdditionPreservesIdentitySessionsCodesAndBusinessAndNewKeyCanLogIn()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        await app.LoginAsync(client);
        var tables = app.Database.Tables().Where(table => table is not ("AspNetUsers" or "AspNetUserPasskeys")).ToArray();
        var before = app.Database.Snapshot(tables);
        var accountBefore = await AccountSnapshotAsync(app);
        var options = await BeginAsync(client);
        Assert.Equal(app.UserId, System.Text.Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(options.GetProperty("user").GetProperty("id").GetString()!)));
        Assert.Contains(options.GetProperty("excludeCredentials").EnumerateArray(), row => row.GetProperty("id").GetString() == WebEncoders.Base64UrlEncode(app.CredentialId));
        var id = RandomNumberGenerator.GetBytes(32);
        using var added = await VerifyAsync(client, app.Attestation(options, id));
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        Assert.DoesNotContain(added.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith(SessionCookieEvents.CookieName + "=", StringComparison.Ordinal));
        Assert.Equal(2L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUserPasskeys"));
        Assert.Equal(1L, app.Database.Scalar("SELECT IsEnabled FROM AspNetUsers"));
        Assert.Equal(before, app.Database.Snapshot(tables));
        Assert.Equal(accountBefore, await AccountSnapshotAsync(app));
        using var another = app.Client();
        var login = await app.BeginAsync(another);
        using var authenticated = await app.VerifyAsync(another, app.AssertionFor(login, app.UserId, id));
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
        using var original = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.OK, original.StatusCode);
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("challenge")]
    [InlineData("rp")]
    [InlineData("uv")]
    [InlineData("cross-origin")]
    public async Task InvalidCreationDoesNotChangeAnythingAndConsumesChallenge(string corruption)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        await app.LoginAsync(client);
        var options = await BeginAsync(client);
        var id = RandomNumberGenerator.GetBytes(32);
        var before = app.Database.Snapshot(app.Database.Tables());
        using var rejected = await VerifyAsync(client, app.Attestation(options, id, corruption));
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        using var replay = await VerifyAsync(client, app.Attestation(options, id));
        Assert.Equal(HttpStatusCode.Forbidden, replay.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Theory]
    [InlineData("disabled", HttpStatusCode.Unauthorized)]
    [InlineData("revoked", HttpStatusCode.Unauthorized)]
    [InlineData("expired", HttpStatusCode.Unauthorized)]
    [InlineData("stale", HttpStatusCode.Forbidden)]
    [InlineData("store", HttpStatusCode.ServiceUnavailable)]
    public async Task PendingAdditionRechecksLiveIdentityAndSession(string change, HttpStatusCode expected)
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
            case "stale": app.Time.Advance(TimeSpan.FromMinutes(5)); break;
            case "store": app.Database.Execute("ALTER TABLE auth_sessions RENAME TO unavailable_sessions"); break;
        }
        var before = app.Database.Snapshot(app.Database.Tables());
        using var result = await VerifyAsync(client, app.Attestation(options, RandomNumberGenerator.GetBytes(32)));
        Assert.Equal(expected, result.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task AdditionIsBrowserSessionAndPurposeBoundAndCannotBeReused()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var owner = app.Client();
        using var second = app.Client();
        await app.LoginAsync(owner);
        await app.LoginAsync(second);
        var otherSession = app.SessionCookie.Split(';')[0];
        await LoginTestApp.CsrfAsync(owner);
        using var begin = await owner.PostAsJsonAsync("/auth/passkeys/options", new { });
        var options = await begin.Content.ReadFromJsonAsync<JsonElement>();
        var additionCookie = begin.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        var credential = app.Attestation(options, RandomNumberGenerator.GetBytes(32));
        await LoginTestApp.CsrfAsync(second);
        using var wrongBrowser = await VerifyAsync(second, credential);
        Assert.Equal(HttpStatusCode.Forbidden, wrongBrowser.StatusCode);
        using var wrongPurpose = await owner.PostAsJsonAsync("/auth/enrollment/verify", new { credential });
        Assert.Equal(HttpStatusCode.BadRequest, wrongPurpose.StatusCode);
        using var copied = app.Client();
        copied.DefaultRequestHeaders.Add("Cookie", otherSession + "; " + additionCookie);
        await LoginTestApp.CsrfAsync(copied);
        var before = app.Database.Snapshot(app.Database.Tables());
        using var wrongSession = await VerifyAsync(copied, credential);
        Assert.Equal(HttpStatusCode.Forbidden, wrongSession.StatusCode);
        using var consumed = await VerifyAsync(owner, credential);
        Assert.Equal(HttpStatusCode.Forbidden, consumed.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task ReplacementAndRestartInvalidatePendingAdditions()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var owner = app.Client();
        await app.LoginAsync(owner);
        var old = await BeginAsync(owner);
        await BeginAsync(owner);
        using var replaced = await VerifyAsync(owner, app.Attestation(old, RandomNumberGenerator.GetBytes(32)));
        Assert.Equal(HttpStatusCode.Forbidden, replaced.StatusCode);
        using var begin = await owner.PostAsJsonAsync("/auth/passkeys/options", new { });
        var options = await begin.Content.ReadFromJsonAsync<JsonElement>();
        var additionCookie = begin.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        await using var restarted = app.NewFactory();
        using var restored = app.Client(restarted);
        restored.DefaultRequestHeaders.Add("Cookie", app.SessionCookie.Split(';')[0] + "; " + additionCookie);
        await LoginTestApp.CsrfAsync(restored);
        var before = app.Database.Snapshot(app.Database.Tables());
        using var lost = await VerifyAsync(restored, app.Attestation(options, RandomNumberGenerator.GetBytes(32)));
        Assert.Equal(HttpStatusCode.Forbidden, lost.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicateCredentialCannotBeAddedOrTransferredFromAnotherAccount(bool anotherAccount)
    {
        await using var app = await LoginTestApp.CreateAsync();
        var id = anotherAccount ? (await app.AddAccountAsync(2)).CredentialId : app.CredentialId;
        using var client = app.Client();
        await app.LoginAsync(client);
        var options = await BeginAsync(client);
        var before = app.Database.Snapshot(app.Database.Tables());
        using var duplicate = await VerifyAsync(client, app.Attestation(options, id));
        Assert.Equal(HttpStatusCode.Forbidden, duplicate.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Theory]
    [InlineData("insert")]
    [InlineData("account")]
    [InlineData("revoked-during-save")]
    public async Task AdditionFailureRollsBackAndAllowsRetry(string failure)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        await app.LoginAsync(client);
        var trigger = failure switch
        {
            "insert" => "BEFORE INSERT ON AspNetUserPasskeys BEGIN SELECT RAISE(ABORT,'injected'); END",
            "account" => "BEFORE UPDATE ON AspNetUsers BEGIN SELECT RAISE(ABORT,'injected'); END",
            _ => "AFTER INSERT ON AspNetUserPasskeys BEGIN UPDATE auth_sessions SET RevokedAtUtc=CreatedAtUtc; END"
        };
        app.Database.Execute("CREATE TRIGGER fail_addition " + trigger);
        var before = app.Database.Snapshot(app.Database.Tables());
        var options = await BeginAsync(client);
        var id = RandomNumberGenerator.GetBytes(32);
        using var failed = await VerifyAsync(client, app.Attestation(options, id));
        Assert.Equal(failure == "revoked-during-save" ? HttpStatusCode.Forbidden : HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        app.Database.Execute("DROP TRIGGER fail_addition");
        options = await BeginAsync(client);
        using var retry = await VerifyAsync(client, app.Attestation(options, id));
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Fact]
    public async Task SuccessfulDeletionRevokesEveryOwnSessionAndRemainingKeyCanLogIn()
    {
        await using var app = await LoginTestApp.CreateAsync();
        var other = await app.AddAccountAsync(2);
        using var first = app.Client();
        using var second = app.Client();
        using var unrelated = app.Client();
        await app.LoginAsync(first);
        var originalCookie = app.SessionCookie.Split(';')[0];
        var addedId = await AddAsync(app, first);
        await app.LoginAsync(second);
        var otherOptions = await app.BeginAsync(unrelated);
        using var otherLogin = await app.VerifyAsync(unrelated, app.AssertionFor(otherOptions, other.UserId, other.CredentialId));
        Assert.Equal(HttpStatusCode.OK, otherLogin.StatusCode);
        var tables = app.Database.Tables().Where(table => table is not ("AspNetUsers" or "AspNetUserPasskeys" or "auth_sessions")).ToArray();
        var before = app.Database.Snapshot(tables);
        await LoginTestApp.CsrfAsync(first);
        using var removed = await RemoveAsync(first, app.CredentialId);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Contains(removed.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith(SessionCookieEvents.CookieName + "=;", StringComparison.Ordinal));
        Assert.Equal(0L, app.Database.Scalar($"SELECT COUNT(*) FROM auth_sessions WHERE AuthUserId='{app.UserId}' AND RevokedAtUtc IS NULL"));
        Assert.Equal(1L, app.Database.Scalar($"SELECT COUNT(*) FROM AspNetUserPasskeys WHERE UserId='{app.UserId}'"));
        Assert.Equal(1L, app.Database.Scalar($"SELECT IsEnabled FROM AspNetUsers WHERE Id='{app.UserId}'"));
        Assert.Equal(before, app.Database.Snapshot(tables));
        using var secondMe = await second.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, secondMe.StatusCode);
        using var otherMe = await unrelated.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.OK, otherMe.StatusCode);
        using var copied = app.Client();
        copied.DefaultRequestHeaders.Add("Cookie", originalCookie);
        using var replay = await copied.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        var options = await app.BeginAsync(first);
        using var deletedKey = await app.VerifyAsync(first, app.Assertion(options));
        Assert.Equal(HttpStatusCode.Unauthorized, deletedKey.StatusCode);
        options = await app.BeginAsync(first);
        using var remainingKey = await app.VerifyAsync(first, app.AssertionFor(options, app.UserId, addedId));
        Assert.Equal(HttpStatusCode.OK, remainingKey.StatusCode);
    }

    [Fact]
    public async Task LastOtherAndMissingKeysCannotBeDeletedOrExposeOtherOwners()
    {
        await using var app = await LoginTestApp.CreateAsync();
        var other = await app.AddAccountAsync(2);
        using var client = app.Client();
        await app.LoginAsync(client);
        await LoginTestApp.CsrfAsync(client);
        var before = app.Database.Snapshot(app.Database.Tables());
        using var last = await RemoveAsync(client, app.CredentialId);
        Assert.Equal(HttpStatusCode.Conflict, last.StatusCode);
        using var foreign = await RemoveAsync(client, other.CredentialId);
        using var missing = await RemoveAsync(client, RandomNumberGenerator.GetBytes(32));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(await missing.Content.ReadAsStringAsync(), await foreign.Content.ReadAsStringAsync());
        using var malformed = await client.DeleteAsync("/auth/passkeys/A");
        Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Theory]
    [InlineData("AspNetUserPasskeys", "DELETE")]
    [InlineData("auth_sessions", "UPDATE")]
    public async Task DeletionFailurePreservesKeyAndAllSessionsAndCookie(string table, string operation)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        await app.LoginAsync(client);
        await AddAsync(app, client);
        app.Database.Execute($"CREATE TRIGGER fail_deletion BEFORE {operation} ON {table} BEGIN SELECT RAISE(ABORT,'injected'); END");
        var before = app.Database.Snapshot(app.Database.Tables());
        using var failed = await RemoveAsync(client, app.CredentialId);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.False(failed.Headers.Contains("Set-Cookie"));
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        app.Database.Execute("DROP TRIGGER fail_deletion");
        using var retry = await RemoveAsync(client, app.CredentialId);
        Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
    }

    [Fact]
    public async Task ConcurrentDeletionsCannotRemoveBothKeys()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var first = app.Client();
        using var second = app.Client();
        await app.LoginAsync(first);
        var addedId = await AddAsync(app, first);
        await app.LoginAsync(second);
        await LoginTestApp.CsrfAsync(second);
        var results = await Task.WhenAll(RemoveAsync(first, app.CredentialId), RemoveAsync(second, addedId));
        try
        {
            Assert.Single(results, response => response.StatusCode == HttpStatusCode.NoContent);
            Assert.Single(results, response => response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.Conflict);
            Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUserPasskeys"));
            Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM auth_sessions WHERE RevokedAtUtc IS NULL"));
        }
        finally { foreach (var response in results) response.Dispose(); }
    }

    [Fact]
    public async Task ChangesRequireSessionFreshVerificationCsrfAndSameOrigin()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var anonymous = app.Client();
        using var noLogin = await anonymous.PostAsJsonAsync("/auth/passkeys/options", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, noLogin.StatusCode);
        using var client = app.Client();
        await app.LoginAsync(client);
        using var staleCsrf = await client.PostAsJsonAsync("/auth/passkeys/options", new { });
        Assert.Equal(HttpStatusCode.BadRequest, staleCsrf.StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        using var missingCsrf = await RemoveAsync(client, app.CredentialId);
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        await LoginTestApp.CsrfAsync(client);
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "https://other.example.test");
        using var wrongOrigin = await RemoveAsync(client, app.CredentialId);
        Assert.Equal(HttpStatusCode.Forbidden, wrongOrigin.StatusCode);
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", LoginTestApp.Origin);
        app.Time.Advance(TimeSpan.FromMinutes(5));
        var before = app.Database.Snapshot(app.Database.Tables());
        using var oldVerification = await client.PostAsJsonAsync("/auth/passkeys/options", new { });
        using var oldRemoval = await RemoveAsync(client, app.CredentialId);
        Assert.Equal(HttpStatusCode.Forbidden, oldVerification.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, oldRemoval.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task ConcurrentAdditionCanConsumeTheSameChallengeOnlyOnce()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        await app.LoginAsync(client);
        var options = await BeginAsync(client);
        var credential = app.Attestation(options, RandomNumberGenerator.GetBytes(32));
        var results = await Task.WhenAll(VerifyAsync(client, credential), VerifyAsync(client, credential));
        try
        {
            Assert.Single(results, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(results, response => response.StatusCode == HttpStatusCode.Forbidden);
            Assert.Equal(2L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUserPasskeys"));
        }
        finally { foreach (var response in results) response.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VerificationOrSessionExpiryDuringAttestationRollsBackAddedKey(bool sessionExpires)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        await app.LoginAsync(client);
        var me = await client.GetFromJsonAsync<JsonElement>("/auth/me");
        var expiry = me.GetProperty("expiresAtUtc").GetDateTimeOffset();
        if (sessionExpires)
        {
            app.Time.Set(expiry.AddMinutes(-1));
            await LoginTestApp.CsrfAsync(client);
            using var begin = await client.PostAsJsonAsync("/auth/reauthentication/options", new { });
            var options = await begin.Content.ReadFromJsonAsync<JsonElement>();
            using var verify = await client.PostAsJsonAsync("/auth/reauthentication/verify", new { credential = app.Assertion(options) });
            Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        }
        else app.Time.Advance(TimeSpan.FromMinutes(4.5));
        await using var delayed = app.NewFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddScoped<PasskeyHandler<AuthUser>>();
            services.AddScoped<IPasskeyHandler<AuthUser>>(provider => new DelayedAttestationHandler(
                provider.GetRequiredService<PasskeyHandler<AuthUser>>(), () =>
                {
                    if (sessionExpires) app.Time.Set(expiry);
                    else app.Time.Advance(TimeSpan.FromSeconds(30));
                }));
        }));
        using var current = app.Client(delayed);
        current.DefaultRequestHeaders.Add("Cookie", app.SessionCookie.Split(';')[0]);
        var addition = await BeginAsync(current);
        var before = app.Database.Snapshot(app.Database.Tables());
        using var rejected = await VerifyAsync(current, app.Attestation(addition, RandomNumberGenerator.GetBytes(32)));
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task AdditionAndDeletionShareTheExistingAuthenticationRateLimit()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        await app.LoginAsync(client);
        await using var isolated = app.NewFactory();
        using var bounded = app.Client(isolated);
        bounded.DefaultRequestHeaders.Add("Cookie", app.SessionCookie.Split(';')[0]);
        await LoginTestApp.CsrfAsync(bounded);
        for (var index = 0; index < 20; index++)
        {
            using var response = index % 2 == 0
                ? await bounded.PostAsJsonAsync("/auth/passkeys/options", new { })
                : await RemoveAsync(bounded, app.CredentialId);
            Assert.Equal(index % 2 == 0 ? HttpStatusCode.OK : HttpStatusCode.Conflict, response.StatusCode);
        }
        using var limited = await bounded.PostAsJsonAsync("/auth/login/options", new { });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task PendingAdditionCannotRestoreSessionsRevokedByDeletion()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        await app.LoginAsync(client);
        await AddAsync(app, client);
        var options = await BeginAsync(client);
        var add = VerifyAsync(client, app.Attestation(options, RandomNumberGenerator.GetBytes(32)));
        var remove = RemoveAsync(client, app.CredentialId);
        await Task.WhenAll(add, remove);
        using var added = await add;
        using var removed = await remove;
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Contains(added.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized });
        Assert.Contains((long)app.Database.Scalar("SELECT COUNT(*) FROM AspNetUserPasskeys")!, new[] { 1L, 2L });
        Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM auth_sessions WHERE RevokedAtUtc IS NULL"));
        using var me = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    /// <summary>現在の本人用の追加オプションを取得する。</summary>
    private static async Task<JsonElement> BeginAsync(HttpClient client)
    {
        await LoginTestApp.CsrfAsync(client);
        using var response = await client.PostAsJsonAsync("/auth/passkeys/options", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>標準の登録応答を本人のパスキー追加受付へ送る。</summary>
    private static Task<HttpResponseMessage> VerifyAsync(HttpClient client, object credential) =>
        client.PostAsJsonAsync("/auth/passkeys/verify", new { credential });

    /// <summary>ログイン可能な追加パスキーを本人へ登録する。</summary>
    private static async Task<byte[]> AddAsync(LoginTestApp app, HttpClient client)
    {
        var options = await BeginAsync(client);
        var id = RandomNumberGenerator.GetBytes(32);
        using var response = await VerifyAsync(client, app.Attestation(options, id));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return id;
    }

    /// <summary>本人のパスキー削除受付へ識別子を送る。</summary>
    private static Task<HttpResponseMessage> RemoveAsync(HttpClient client, byte[] id) =>
        client.DeleteAsync("/auth/passkeys/" + WebEncoders.Base64UrlEncode(id));

    /// <summary>標準Identityが更新する競合検出値を除き、アカウントの全プロパティを比較する。</summary>
    private static async Task<string> AccountSnapshotAsync(LoginTestApp app)
    {
        await using var scope = app.Factory.Services.CreateAsyncScope();
        var accounts = await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Users.AsNoTracking().OrderBy(user => user.Id).ToListAsync();
        foreach (var account in accounts) account.ConcurrencyStamp = null;
        return JsonSerializer.Serialize(accounts);
    }

    /// <summary>標準の登録検証を維持し、検証中にサーバー時刻が期限へ到達する状況を作る。</summary>
    private sealed class DelayedAttestationHandler(IPasskeyHandler<AuthUser> inner, Action advance) : IPasskeyHandler<AuthUser>
    {
        public Task<PasskeyCreationOptionsResult> MakeCreationOptionsAsync(PasskeyUserEntity userEntity, HttpContext http) =>
            inner.MakeCreationOptionsAsync(userEntity, http);
        public Task<PasskeyRequestOptionsResult> MakeRequestOptionsAsync(AuthUser? user, HttpContext http) =>
            inner.MakeRequestOptionsAsync(user, http);
        public Task<PasskeyAssertionResult<AuthUser>> PerformAssertionAsync(PasskeyAssertionContext context) =>
            inner.PerformAssertionAsync(context);
        public async Task<PasskeyAttestationResult> PerformAttestationAsync(PasskeyAttestationContext context)
        {
            var result = await inner.PerformAttestationAsync(context);
            advance();
            return result;
        }
    }
}
