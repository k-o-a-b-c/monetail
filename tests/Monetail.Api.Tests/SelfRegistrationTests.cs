using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Authentication;
using Monetail.Api.Persistence;
using Xunit;
using static Monetail.Api.Tests.SelfRegistrationTestApp;

namespace Monetail.Api.Tests;

/// <summary>匿名自己登録の標準検証・原子保存・濫用防止と既存経路の独立性を確認する。</summary>
public sealed class SelfRegistrationTests
{
    [Fact]
    public async Task OptionsCreateNoRowsAndSuccessfulAccountHasNoPrivilegesOrAutomaticLogin()
    {
        await using var app = await CreateAsync();
        using var client = app.Client();
        var before = app.Database.Snapshot(app.Database.Tables());
        var options = await BeginAsync(client, "  表示名  ");
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        Assert.Equal("表示名", options.GetProperty("user").GetProperty("displayName").GetString());
        Assert.Equal("required", options.GetProperty("authenticatorSelection").GetProperty("residentKey").GetString());
        Assert.Equal("required", options.GetProperty("authenticatorSelection").GetProperty("userVerification").GetString());
        using var response = await VerifyAsync(client, PasskeyEnrollmentTests.Credential(options));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(response.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith(SessionCookieEvents.CookieName + "="));
        Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUsers WHERE IsEnabled=1 AND DisplayName='表示名' AND Email IS NULL AND PasswordHash IS NULL"));
        Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUserPasskeys"));
        Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM auth_self_registration_completions"));
        foreach (var table in new[] { "AspNetUserRoles", "auth_sessions", "auth_enrollment_codes", "auth_account_creation_requests", "ledgers", "ledger_members", "ledger_people" })
            Assert.Equal(0L, app.Database.Scalar($"SELECT COUNT(*) FROM {table}"));
        using var me = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        using var duplicate = await VerifyAsync(client, PasskeyEnrollmentTests.Credential(options));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
    }

    [Fact]
    public async Task CreatedPasskeyCanUseNormalLoginWithItsPrivateKey()
    {
        await using var app = await CreateAsync();
        using var client = app.Client();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var id = RandomNumberGenerator.GetBytes(32);
        var options = await BeginAsync(client);
        using var registered = await VerifyAsync(client, PasskeyEnrollmentTests.Credential(options, id: id, publicParameters: key.ExportParameters(false)));
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        await LoginTestApp.CsrfAsync(client);
        using var begin = await client.PostAsJsonAsync("/auth/login/options", new { timeZone = "Asia/Tokyo" });
        Assert.Equal(HttpStatusCode.OK, begin.StatusCode);
        var login = await begin.Content.ReadFromJsonAsync<JsonElement>();
        var clientData = JsonSerializer.SerializeToUtf8Bytes(new { type = "webauthn.get", challenge = login.GetProperty("challenge").GetString(), origin = LoginTestApp.Origin, crossOrigin = false });
        byte[] authData = [.. SHA256.HashData(Encoding.UTF8.GetBytes("monetail.example.test")), 0x05, 0, 0, 0, 0];
        var signature = key.SignData([.. authData, .. SHA256.HashData(clientData)], HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        using var loggedIn = await client.PostAsJsonAsync("/auth/login/verify", new { credential = new
        {
            id = WebEncoders.Base64UrlEncode(id), rawId = WebEncoders.Base64UrlEncode(id), type = "public-key",
            response = new { authenticatorData = WebEncoders.Base64UrlEncode(authData), clientDataJSON = WebEncoders.Base64UrlEncode(clientData), signature = WebEncoders.Base64UrlEncode(signature), userHandle = options.GetProperty("user").GetProperty("id").GetString() },
            clientExtensionResults = new { }
        } });
        Assert.Equal(HttpStatusCode.OK, loggedIn.StatusCode);
        Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM auth_sessions"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("too-long")]
    public async Task InvalidNamesNeverAllocateAnAccount(string? name)
    {
        await using var app = await CreateAsync();
        using var client = app.Client();
        await LoginTestApp.CsrfAsync(client);
        using var response = await client.PostAsJsonAsync(Root + "/options", new { displayName = name == "too-long" ? new string('名', 101) : name });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertEmpty(app);
    }

    [Fact]
    public async Task SameNamesAndOneHundredUtf16CharactersAreAllowed()
    {
        await using var app = await CreateAsync();
        foreach (var ip in new[] { "192.0.2.1", "192.0.2.2" })
        {
            using var client = app.Client(ip);
            var options = await BeginAsync(client, new string('名', 100));
            using var response = await VerifyAsync(client, PasskeyEnrollmentTests.Credential(options));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        Assert.Equal(2L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUsers"));
    }

    [Theory]
    [InlineData("challenge")]
    [InlineData("origin")]
    [InlineData("rp")]
    [InlineData("uv")]
    [InlineData("cross-origin")]
    public async Task StandardWebAuthnRejectionsConsumeTheChallengeAndLeaveNoRows(string corruption)
    {
        await using var app = await CreateAsync();
        using var client = app.Client();
        var options = await BeginAsync(client);
        using var failed = await VerifyAsync(client, PasskeyEnrollmentTests.Credential(options, corruption));
        Assert.Equal(HttpStatusCode.BadRequest, failed.StatusCode);
        using var retry = await VerifyAsync(client, PasskeyEnrollmentTests.Credential(options));
        Assert.Equal(HttpStatusCode.BadRequest, retry.StatusCode);
        AssertEmpty(app);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"credential\":[]}")]
    [InlineData("{")]
    public async Task MalformedVerificationAlsoConsumesTheChallenge(string body)
    {
        await using var app = await CreateAsync();
        using var client = app.Client();
        var options = await BeginAsync(client);
        using var malformed = await client.PostAsync(Root + "/verify", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        using var replay = await VerifyAsync(client, PasskeyEnrollmentTests.Credential(options));
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        AssertEmpty(app);
    }

    [Fact]
    public async Task DifferentBrowserWrongPurposeExpiryAndReplacedChallengeAreRejected()
    {
        await using var app = await CreateAsync();
        using var client = app.Client();
        var options = await BeginAsync(client);
        using var other = app.Client();
        await LoginTestApp.CsrfAsync(other);
        using var browser = await VerifyAsync(other, PasskeyEnrollmentTests.Credential(options));
        Assert.Equal(HttpStatusCode.BadRequest, browser.StatusCode);
        app.Time.Advance(TimeSpan.FromMinutes(5));
        using var expired = await VerifyAsync(client, PasskeyEnrollmentTests.Credential(options));
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
        var replaced = await BeginAsync(client);
        _ = await BeginAsync(client);
        using var replacement = await VerifyAsync(client, PasskeyEnrollmentTests.Credential(replaced));
        Assert.Equal(HttpStatusCode.BadRequest, replacement.StatusCode);
        var challenge = app.Factory.Services.GetRequiredService<EnrollmentChallenges>();
        var invite = challenge.Add(new("unwritten", "name", [], "synthetic", app.Time.GetUtcNow().AddMinutes(5)), null)!;
        using var wrongPurpose = app.Client();
        await LoginTestApp.CsrfAsync(wrongPurpose);
        wrongPurpose.DefaultRequestHeaders.Add("Cookie", SelfRegistrationChallenges.CookieName + "=" + invite);
        using var wrong = await VerifyAsync(wrongPurpose, PasskeyEnrollmentTests.Credential(options));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.NotNull(challenge.Take(invite));
        AssertEmpty(app);
    }

    [Fact]
    public async Task ConcurrentCompletionOfOneChallengeCommitsOnlyOnce()
    {
        await using var app = await CreateAsync();
        using var client = app.Client();
        var options = await BeginAsync(client);
        var credential = PasskeyEnrollmentTests.Credential(options);
        var responses = await Task.WhenAll(VerifyAsync(client, credential), VerifyAsync(client, credential));
        try { Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest }, responses.Select(r => r.StatusCode).Order().ToArray()); }
        finally { foreach (var response in responses) response.Dispose(); }
        Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM auth_self_registration_completions"));
        Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUsers"));
    }

    [Theory]
    [InlineData("account")]
    [InlineData("passkey")]
    [InlineData("enable")]
    [InlineData("completion")]
    [InlineData("commit")]
    public async Task FailureAtEveryWriteStageRollsBackEverything(string stage)
    {
        await using var app = await CreateAsync(configure: stage == "commit" ? builder => builder.ConfigureServices(services =>
            services.AddDbContext<ApiDbContext>(options => options.AddInterceptors(new FailCommit())) ) : null);
        var operation = stage switch
        {
            "account" => "BEFORE INSERT ON AspNetUsers",
            "passkey" => "BEFORE INSERT ON AspNetUserPasskeys",
            "enable" => "BEFORE UPDATE OF IsEnabled ON AspNetUsers WHEN NEW.IsEnabled=1",
            _ => "BEFORE INSERT ON auth_self_registration_completions"
        };
        if (stage != "commit") app.Database.Execute($"CREATE TRIGGER fail_registration {operation} BEGIN SELECT RAISE(ABORT,'synthetic failure'); END");
        using var client = app.Client();
        var options = await BeginAsync(client);
        var before = app.Database.Snapshot(app.Database.Tables());
        using var response = await VerifyAsync(client, PasskeyEnrollmentTests.Credential(options));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        AssertEmpty(app);
        using var reused = await VerifyAsync(client, PasskeyEnrollmentTests.Credential(options));
        Assert.Equal(HttpStatusCode.BadRequest, reused.StatusCode);
    }

    [Fact]
    public async Task ConcurrentRegistrationsCannotExceedOneHundred()
    {
        await using var app = await CreateAsync();
        await app.SeedCompletionsAsync(99, app.Time.GetUtcNow());
        using var first = app.Client("192.0.2.1");
        using var second = app.Client("192.0.2.2");
        var a = await BeginAsync(first);
        var b = await BeginAsync(second);
        var responses = await Task.WhenAll(VerifyAsync(first, PasskeyEnrollmentTests.Credential(a)), VerifyAsync(second, PasskeyEnrollmentTests.Credential(b)));
        try { Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.TooManyRequests }, responses.Select(r => r.StatusCode).Order().ToArray()); }
        finally { foreach (var response in responses) response.Dispose(); }
        Assert.Equal(100L, app.Database.Scalar("SELECT COUNT(*) FROM auth_self_registration_completions"));
        Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUsers"));
        Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUserPasskeys"));
    }

    [Fact]
    public async Task DuplicateCredentialAcrossDifferentChallengesLeavesOnlyOneOwner()
    {
        await using var app = await CreateAsync();
        using var a = app.Client("192.0.2.1");
        using var b = app.Client("192.0.2.2");
        var first = await BeginAsync(a);
        var second = await BeginAsync(b);
        var id = RandomNumberGenerator.GetBytes(32);
        var responses = await Task.WhenAll(VerifyAsync(a, PasskeyEnrollmentTests.Credential(first, id: id)), VerifyAsync(b, PasskeyEnrollmentTests.Credential(second, id: id)));
        try { Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest }, responses.Select(r => r.StatusCode).Order().ToArray()); }
        finally { foreach (var response in responses) response.Dispose(); }
        Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUsers"));
        Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM auth_self_registration_completions"));
    }

    [Fact]
    public async Task SuccessWindowPersistsAcrossRestartAndResetAndDoesNotResetAtMidnight()
    {
        await using var app = await CreateAsync();
        var midnight = new DateTimeOffset(app.Time.GetUtcNow().UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
        app.Time.Advance(midnight - app.Time.GetUtcNow() - TimeSpan.FromMinutes(1));
        await app.SeedCompletionsAsync(100, app.Time.GetUtcNow().AddHours(-23));
        await app.SeedCompletionsAsync(1, app.Time.GetUtcNow().AddHours(-49));
        await app.RestartAsync();
        await using (var scope = app.Factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AuthenticationRevocationService>().ResetAfterRestoreAsync();
        using var client = app.Client();
        var blocked = await BeginAsync(client);
        using var limited = await VerifyAsync(client, PasskeyEnrollmentTests.Credential(blocked));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUsers"));
        app.Time.Advance(TimeSpan.FromMinutes(1));
        var afterMidnight = await BeginAsync(client);
        using var midnightLimited = await VerifyAsync(client, PasskeyEnrollmentTests.Credential(afterMidnight));
        Assert.Equal(HttpStatusCode.TooManyRequests, midnightLimited.StatusCode);
        app.Time.Advance(TimeSpan.FromMinutes(59));
        var allowed = await BeginAsync(client);
        using var created = await VerifyAsync(client, PasskeyEnrollmentTests.Credential(allowed));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        // 24時間ちょうどは窓外、48時間より古い行だけが掃除される。
        Assert.Equal(101L, app.Database.Scalar("SELECT COUNT(*) FROM auth_self_registration_completions"));
    }

    [Fact]
    public async Task RestartDiscardsChallengesAndInvitationModeRejectsBothWithoutDbChanges()
    {
        await using var app = await CreateAsync();
        using var old = app.Client();
        var options = await BeginAsync(old);
        var challenges = app.Factory.Services.GetRequiredService<SelfRegistrationChallenges>();
        var id = challenges.Add(new("candidate", "name", "synthetic", app.Time.GetUtcNow().AddMinutes(5)), null)!;
        await app.RestartAsync();
        using var restarted = app.Client();
        await LoginTestApp.CsrfAsync(restarted);
        restarted.DefaultRequestHeaders.Add("Cookie", SelfRegistrationChallenges.CookieName + "=" + id);
        using var lost = await VerifyAsync(restarted, PasskeyEnrollmentTests.Credential(options));
        Assert.Equal(HttpStatusCode.BadRequest, lost.StatusCode);
        await app.RestartAsync("InvitationOnly");
        using var invitation = app.Client();
        await LoginTestApp.CsrfAsync(invitation);
        using var begin = await invitation.PostAsJsonAsync(Root + "/options", new { displayName = "name" });
        using var verify = await VerifyAsync(invitation, PasskeyEnrollmentTests.Credential(options));
        Assert.Equal(HttpStatusCode.Forbidden, begin.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, verify.StatusCode);
        AssertEmpty(app);
    }

    [Fact]
    public async Task CommonAuthenticationBudgetAndSlidingStartBudgetBothApply()
    {
        await using var app = await CreateAsync();
        using var client = app.Client();
        await LoginTestApp.CsrfAsync(client);
        for (var i = 0; i < 4; i++)
        {
            using var invalid = await client.PostAsJsonAsync(Root + "/options", new { displayName = " " });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using var malformed = await client.PostAsync(Root + "/options", new StringContent("{", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        using var sixth = await client.PostAsJsonAsync(Root + "/options", new { displayName = "name" });
        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
        app.Time.Advance(TimeSpan.FromMinutes(10));
        using var accepted = await client.PostAsJsonAsync(Root + "/options", new { displayName = "name" });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var shared = app.Client("192.0.2.2");
        await LoginTestApp.CsrfAsync(shared);
        for (var i = 0; i < 19; i++)
        {
            using var login = await shared.PostAsJsonAsync("/auth/login/verify", new { credential = new { } });
            Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        }
        using var twentieth = await shared.PostAsJsonAsync(Root + "/options", new { displayName = " " });
        Assert.Equal(HttpStatusCode.BadRequest, twentieth.StatusCode);
        using var next = await VerifyAsync(shared, new { });
        Assert.Equal(HttpStatusCode.TooManyRequests, next.StatusCode);
        AssertEmpty(app);
    }

    [Fact]
    public async Task CapacityIs429AndDoesNotAffectInvitationChallengeCapacity()
    {
        await using var app = await CreateAsync();
        var self = app.Factory.Services.GetRequiredService<SelfRegistrationChallenges>();
        var invite = app.Factory.Services.GetRequiredService<EnrollmentChallenges>();
        for (var i = 0; i < 128; i++) Assert.NotNull(self.Add(new("id", "name", "synthetic", app.Time.GetUtcNow().AddMinutes(5)), null));
        for (var i = 0; i < 1024; i++) Assert.NotNull(invite.Add(new("id", "name", [], "synthetic", app.Time.GetUtcNow().AddMinutes(5)), null));
        using var client = app.Client();
        await LoginTestApp.CsrfAsync(client);
        using var response = await client.PostAsJsonAsync(Root + "/options", new { displayName = "name" });
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        app.Time.Advance(TimeSpan.FromMinutes(5));
        using var available = await client.PostAsJsonAsync(Root + "/options", new { displayName = "name" });
        Assert.Equal(HttpStatusCode.OK, available.StatusCode);
        AssertEmpty(app);
    }

    [Fact]
    public async Task IpStateIsBoundedAndUntrustedForwardedAddressesCannotBypassIt()
    {
        await using var app = await CreateAsync();
        using var client = app.Client();
        await LoginTestApp.CsrfAsync(client);
        for (var i = 0; i < 6; i++)
        {
            client.DefaultRequestHeaders.Remove("X-Forwarded-For");
            client.DefaultRequestHeaders.Add("X-Forwarded-For", $"2001:db8::{i + 1:x}");
            using var response = await client.PostAsJsonAsync(Root + "/options", new { displayName = " " });
            Assert.Equal(i < 5 ? HttpStatusCode.BadRequest : HttpStatusCode.TooManyRequests, response.StatusCode);
        }
        app.Time.Advance(TimeSpan.FromMinutes(10));
        var limiter = app.Factory.Services.GetRequiredService<SelfRegistrationAttemptLimiter>();
        for (var i = 0; i < 4096; i++) Assert.True(limiter.TryAcquire($"2001:db8::{i:x}"));
        using var full = await client.PostAsJsonAsync(Root + "/options", new { displayName = "name" });
        Assert.Equal(HttpStatusCode.TooManyRequests, full.StatusCode);
        using var verify = await VerifyAsync(client, new { });
        Assert.Equal(HttpStatusCode.BadRequest, verify.StatusCode);
        app.Time.Advance(TimeSpan.FromMinutes(10));
        Assert.True(limiter.TryAcquire("192.0.2.1"));
        AssertEmpty(app);
    }

    [Fact]
    public async Task HttpsOriginCsrfAndBodySizeArePreserved()
    {
        await using var app = await CreateAsync();
        using var client = app.Client();
        using var csrf = await client.PostAsJsonAsync(Root + "/options", new { displayName = "name" });
        Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
        await LoginTestApp.CsrfAsync(client);
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "https://wrong.example.test");
        using var origin = await client.PostAsJsonAsync(Root + "/options", new { displayName = "name" });
        Assert.Equal(HttpStatusCode.Forbidden, origin.StatusCode);
        using var insecure = await client.PostAsJsonAsync("http://monetail.example.test" + Root + "/options", new { displayName = "name" });
        Assert.Equal(HttpStatusCode.BadRequest, insecure.StatusCode);
        using var wrongHost = await client.PostAsJsonAsync("https://wrong.example.test" + Root + "/options", new { displayName = "name" });
        Assert.Equal(HttpStatusCode.BadRequest, wrongHost.StatusCode);
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", LoginTestApp.Origin);
        using var large = await client.PostAsync(Root + "/options", new StringContent(new string('x', 65537), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, large.StatusCode);
        AssertEmpty(app);
    }

    [Fact]
    public async Task RouteCasingAndTrailingSlashCannotBypassStartBudget()
    {
        await using var app = await CreateAsync();
        using var client = app.Client();
        await LoginTestApp.CsrfAsync(client);
        for (var i = 0; i < 6; i++)
        {
            var path = i % 2 == 0 ? Root + "/OPTIONS/" : Root + "/options";
            using var response = await client.PostAsJsonAsync(path, new { displayName = " " });
            Assert.Equal(i < 5 ? HttpStatusCode.BadRequest : HttpStatusCode.TooManyRequests, response.StatusCode);
        }
        AssertEmpty(app);
    }

    [Fact]
    public async Task OnlyTrustedProxyCanChooseTheEffectiveClientIp()
    {
        await using var app = await CreateAsync(configure: builder => builder.UseSetting("Passkeys:TrustedProxies:0", "127.0.0.1"));
        using var client = app.Client("127.0.0.1");
        await LoginTestApp.CsrfAsync(client);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "192.0.2.10");
        for (var i = 0; i < 6; i++)
        {
            using var response = await client.PostAsJsonAsync(Root + "/options", new { displayName = " " });
            Assert.Equal(i < 5 ? HttpStatusCode.BadRequest : HttpStatusCode.TooManyRequests, response.StatusCode);
        }
        client.DefaultRequestHeaders.Remove("X-Forwarded-For");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "192.0.2.11");
        using var independent = await client.PostAsJsonAsync(Root + "/options", new { displayName = " " });
        Assert.Equal(HttpStatusCode.BadRequest, independent.StatusCode);
    }

    [Fact]
    public void SlidingWindowReleasesOnlyExpiredAttemptsAndCapacityRecovers()
    {
        var time = new RegistrationTime();
        var limiter = new SelfRegistrationAttemptLimiter(time);
        Assert.True(limiter.TryAcquire("192.0.2.1"));
        time.Advance(TimeSpan.FromMinutes(5));
        for (var i = 0; i < 4; i++) Assert.True(limiter.TryAcquire("192.0.2.1"));
        time.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromTicks(1));
        Assert.False(limiter.TryAcquire("192.0.2.1"));
        time.Advance(TimeSpan.FromTicks(1));
        Assert.True(limiter.TryAcquire("192.0.2.1"));
        Assert.False(limiter.TryAcquire("192.0.2.1"));
        var fresh = new SelfRegistrationAttemptLimiter(time);
        for (var i = 0; i < 4096; i++) Assert.True(fresh.TryAcquire($"2001:db8::{i:x}"));
        Assert.False(fresh.TryAcquire("192.0.2.1"));
        Assert.True(fresh.TryAcquire("2001:db8::0"));
        time.Advance(TimeSpan.FromMinutes(10));
        Assert.True(fresh.TryAcquire("192.0.2.1"));
    }

    [Fact]
    public async Task SqliteWriteLockFailsClosedWithoutReusingChallenge()
    {
        await using var app = await CreateAsync();
        // この検証だけ接続の待機時間を短縮し、製品の接続設定は変えない。
        await using var factory = app.NewFactory(configure: builder => builder.UseSetting("ConnectionStrings:Monetail",
            new SqliteConnectionStringBuilder(app.Database.ConnectionString) { DefaultTimeout = 1 }.ToString()));
        using var client = app.Client(factory: factory);
        var options = await BeginAsync(client);
        using (var locked = app.Database.OpenForeignKeyConnection())
        using (var transaction = locked.BeginTransaction())
        {
            using var failure = await VerifyAsync(client, PasskeyEnrollmentTests.Credential(options));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failure.StatusCode);
        }
        using var retry = await VerifyAsync(client, PasskeyEnrollmentTests.Credential(options));
        Assert.Equal(HttpStatusCode.BadRequest, retry.StatusCode);
        AssertEmpty(app);
    }

    private static void AssertEmpty(SelfRegistrationTestApp app)
    {
        foreach (var table in new[] { "AspNetUsers", "AspNetUserPasskeys", "auth_self_registration_completions", "auth_sessions" })
            Assert.Equal(0L, app.Database.Scalar($"SELECT COUNT(*) FROM {table}"));
    }

    private sealed class FailCommit : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default) => throw new InvalidOperationException("synthetic commit failure");
    }
}
