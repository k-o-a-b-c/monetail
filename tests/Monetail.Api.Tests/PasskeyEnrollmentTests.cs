using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Authentication;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>実Identity検証とSQLiteで、登録の対象固定・単回性・原子性を検証する。</summary>
public class PasskeyEnrollmentTests
{
    [Fact]
    public async Task RegistrationUsesIdentityAndPreservesAllLegacyDataWithoutSigningIn()
    {
        await using var app = await EnrollmentApp.CreateAsync();
        var tables = app.Database.Tables().Where(table => !table.StartsWith("AspNet") &&
            !table.StartsWith("auth_") && !table.StartsWith("__EF")).ToArray();
        var before = app.Database.Snapshot(tables);
        using var client = app.Client();
        var options = await app.BeginAsync(client);
        Assert.Equal("monetail.example.test", options.GetProperty("rp").GetProperty("id").GetString());
        Assert.Equal("required", options.GetProperty("authenticatorSelection").GetProperty("residentKey").GetString());
        Assert.Equal("required", options.GetProperty("authenticatorSelection").GetProperty("userVerification").GetString());
        Assert.Equal(app.Code.AuthUserId, Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(options.GetProperty("user").GetProperty("id").GetString()!)));
        Assert.Equal("同じ名前", options.GetProperty("user").GetProperty("displayName").GetString());
        using var response = await app.VerifyAsync(client, Credential(options));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(response.Headers.GetValues("Set-Cookie"), cookie => cookie.Contains("Identity.Application"));
        Assert.Equal(1L, app.Database.Scalar("SELECT IsEnabled FROM AspNetUsers"));
        Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUserPasskeys"));
        Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM auth_enrollment_codes WHERE ConsumedAtUtc IS NOT NULL"));
        Assert.Equal(1L, app.Database.Scalar("SELECT user_code FROM auth_user_links"));
        Assert.Equal(before, app.Database.Snapshot(tables));
        using var replay = await app.VerifyAsync(client, Credential(options));
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("expired")]
    [InlineData("revoked")]
    [InlineData("consumed")]
    [InlineData("purpose")]
    public async Task InvalidCodesReturnTheSameResponseWithoutChangingDatabase(string mode)
    {
        await using var app = await EnrollmentApp.CreateAsync();
        switch (mode)
        {
            case "expired": app.Time.Advance(TimeSpan.FromMinutes(10)); break;
            case "revoked": app.Database.Execute("UPDATE auth_enrollment_codes SET RevokedAtUtc=CreatedAtUtc"); break;
            case "consumed": app.Database.Execute("UPDATE auth_enrollment_codes SET ConsumedAtUtc=CreatedAtUtc"); break;
            case "purpose": app.Database.Execute("UPDATE auth_enrollment_codes SET Purpose='other'"); break;
        }
        var before = app.Database.Snapshot(app.Database.Tables());
        using var client = app.Client();
        await app.CsrfAsync(client);
        using var invalid = await client.PostAsJsonAsync("/auth/enrollment/options", new { code = "invalid" });
        using var response = await client.PostAsJsonAsync("/auth/enrollment/options", new { code = mode == "missing" ? new string('A', 43) : app.Code.Code });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(await invalid.Content.ReadAsStringAsync(), await response.Content.ReadAsStringAsync());
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("challenge")]
    [InlineData("rp")]
    [InlineData("uv")]
    [InlineData("cross-origin")]
    public async Task IdentityRejectsInvalidWebAuthnAndTheChallengeCannotBeReused(string corruption)
    {
        await using var app = await EnrollmentApp.CreateAsync();
        using var client = app.Client();
        var options = await app.BeginAsync(client);
        using var response = await app.VerifyAsync(client, Credential(options, corruption));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var replay = await app.VerifyAsync(client, Credential(options));
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        AssertUnregistered(app);
        var retry = await app.BeginAsync(client);
        using var valid = await app.VerifyAsync(client, Credential(retry));
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
    }

    [Theory]
    [InlineData("reissue")]
    [InlineData("expired")]
    [InlineData("challenge-expired")]
    [InlineData("disabled-code")]
    [InlineData("link")]
    [InlineData("person")]
    [InlineData("enabled")]
    public async Task FinalizationRechecksCurrentCodeAccountPersonAndChallenge(string change)
    {
        await using var app = await EnrollmentApp.CreateAsync();
        using var client = app.Client();
        if (change == "expired") app.Time.Advance(TimeSpan.FromMinutes(9));
        var options = await app.BeginAsync(client);
        switch (change)
        {
            case "reissue":
                await using (var scope = app.Factory.Services.CreateAsyncScope())
                    await scope.ServiceProvider.GetRequiredService<AdminProvisioningService>().IssueEnrollmentCodeAsync(1);
                break;
            case "expired": app.Time.Advance(TimeSpan.FromMinutes(1)); break;
            case "challenge-expired": app.Time.Advance(TimeSpan.FromMinutes(5)); break;
            case "disabled-code": app.Database.Execute("UPDATE auth_enrollment_codes SET RevokedAtUtc=CreatedAtUtc"); break;
            case "link": app.Database.Execute("UPDATE auth_user_links SET user_code=2"); break;
            case "person": app.Database.Execute("DELETE FROM m_user WHERE code=1"); break;
            case "enabled": app.Database.Execute("UPDATE AspNetUsers SET IsEnabled=1"); break;
        }
        var before = app.Database.Snapshot(app.Database.Tables());
        using var response = await app.VerifyAsync(client, Credential(options));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task ChallengesAreBoundToBrowserAndReplacementAndRestartInvalidateThem()
    {
        await using var app = await EnrollmentApp.CreateAsync();
        using var owner = app.Client();
        var options = await app.BeginAsync(owner);
        using var other = app.Client();
        await app.CsrfAsync(other);
        using var stolen = await app.VerifyAsync(other, Credential(options));
        Assert.Equal(HttpStatusCode.BadRequest, stolen.StatusCode);
        await app.BeginAsync(owner);
        using var old = await app.VerifyAsync(owner, Credential(options));
        Assert.Equal(HttpStatusCode.BadRequest, old.StatusCode);
        options = await app.BeginAsync(owner);
        await using var restarted = app.NewFactory();
        using var restartedClient = restarted.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(EnrollmentApp.Origin) });
        restartedClient.DefaultRequestHeaders.Add("Origin", EnrollmentApp.Origin);
        restartedClient.DefaultRequestHeaders.Add("Cookie", app.ChallengeCookie);
        await app.CsrfAsync(restartedClient);
        using var restart = await app.VerifyAsync(restartedClient, Credential(options));
        Assert.Equal(HttpStatusCode.BadRequest, restart.StatusCode);
        AssertUnregistered(app);
    }

    [Theory]
    [InlineData("AspNetUserPasskeys", "INSERT")]
    [InlineData("AspNetUsers", "UPDATE")]
    [InlineData("auth_enrollment_codes", "UPDATE")]
    public async Task DatabaseFailureRollsBackPasskeyEnablementAndCodeConsumption(string table, string operation)
    {
        await using var app = await EnrollmentApp.CreateAsync();
        app.Database.Execute($"CREATE TRIGGER fail_registration BEFORE {operation} ON {table} BEGIN SELECT RAISE(ABORT,'injected'); END");
        using var client = app.Client();
        var options = await app.BeginAsync(client);
        using var response = await app.VerifyAsync(client, Credential(options));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AssertUnregistered(app);
        app.Database.Execute("DROP TRIGGER fail_registration");
        options = await app.BeginAsync(client);
        using var retry = await app.VerifyAsync(client, Credential(options));
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Fact]
    public async Task TwoBrowserRegistrationsCanOnlyCommitOnePasskey()
    {
        await using var app = await EnrollmentApp.CreateAsync();
        using var first = app.Client();
        using var second = app.Client();
        var firstOptions = await app.BeginAsync(first);
        var secondOptions = await app.BeginAsync(second);
        var responses = await Task.WhenAll(app.VerifyAsync(first, Credential(firstOptions)), app.VerifyAsync(second, Credential(secondOptions)));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.BadRequest);
            Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUserPasskeys"));
            Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM auth_enrollment_codes WHERE ConsumedAtUtc IS NOT NULL"));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task SameBrowserChallengeCanOnlyBeConsumedOnceConcurrently()
    {
        await using var app = await EnrollmentApp.CreateAsync();
        using var client = app.Client();
        var options = await app.BeginAsync(client);
        var credential = Credential(options);
        var responses = await Task.WhenAll(app.VerifyAsync(client, credential), app.VerifyAsync(client, credential));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.BadRequest);
            Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUserPasskeys"));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task CredentialRegisteredToAnotherAccountCannotBeReassigned()
    {
        await using var app = await EnrollmentApp.CreateAsync();
        using var first = app.Client();
        var options = await app.BeginAsync(first);
        var credentialId = RandomNumberGenerator.GetBytes(32);
        using var registered = await app.VerifyAsync(first, Credential(options, id: credentialId));
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        IssuedEnrollmentCode secondCode;
        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AuthUser>>();
            var user = new AuthUser { Id = "second-account", UserName = "second-account" };
            Assert.True((await users.CreateAsync(user)).Succeeded);
            var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            context.UserLinks.Add(new AuthUserLink { AuthUserId = user.Id, UserCode = 2 });
            await context.SaveChangesAsync();
            secondCode = await scope.ServiceProvider.GetRequiredService<AdminProvisioningService>().IssueEnrollmentCodeAsync(2);
        }
        using var second = app.Client();
        await app.CsrfAsync(second);
        using var begin = await second.PostAsJsonAsync("/auth/enrollment/options", new { code = secondCode.Code });
        Assert.Equal(HttpStatusCode.OK, begin.StatusCode);
        var secondOptions = await begin.Content.ReadFromJsonAsync<JsonElement>();
        var before = app.Database.Snapshot(app.Database.Tables());
        using var response = await app.VerifyAsync(second, Credential(secondOptions, id: credentialId));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task CsrfHttpsOriginBodyLimitAndRateLimitAreEnforced()
    {
        await using var app = await EnrollmentApp.CreateAsync();
        using var client = app.Client();
        using var noCsrf = await client.PostAsJsonAsync("/auth/enrollment/options", new { code = app.Code.Code });
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        await app.CsrfAsync(client);
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "https://other.example.test");
        using var origin = await client.PostAsJsonAsync("/auth/enrollment/options", new { code = app.Code.Code });
        Assert.Equal(HttpStatusCode.Forbidden, origin.StatusCode);
        using var http = app.Client("http://monetail.example.test");
        http.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        using var insecure = await http.GetAsync("/auth/enroll");
        Assert.Equal(HttpStatusCode.BadRequest, insecure.StatusCode);
        using var host = await client.GetAsync("https://other.example.test/auth/enroll");
        Assert.Equal(HttpStatusCode.BadRequest, host.StatusCode);
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", EnrollmentApp.Origin);
        using var big = await client.PostAsync("/auth/enrollment/options", new StringContent(new string('X', 65537), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, big.StatusCode);
        for (var index = 0; index < 19; index++)
        {
            using var response = await client.PostAsJsonAsync("/auth/enrollment/options", new { code = "invalid" });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using var limited = await client.PostAsJsonAsync("/auth/enrollment/options", new { code = app.Code.Code });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        AssertUnregistered(app);
    }

    [Fact]
    public async Task PagesAreNoStoreAndOnlyExplicitRegistrationResourcesArePublic()
    {
        await using var app = await EnrollmentApp.CreateAsync();
        using var client = app.Client();
        foreach (var path in new[] { "/auth/enroll", "/auth/enroll.js", "/auth/csrf" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        }
        // 最初の取得でCookieが設定済みの場合は再発行しないため、別ブラウザーで属性を確認する。
        using var fresh = app.Client();
        using var initialCsrf = await fresh.GetAsync("/auth/csrf");
        var cookie = initialCsrf.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains("__Host-Monetail.Csrf=", cookie);
        Assert.Contains("secure", cookie);
        Assert.Contains("httponly", cookie);
        Assert.Contains("samesite=lax", cookie);
        Assert.Contains("path=/", cookie);
        Assert.DoesNotContain("domain=", cookie);
        foreach (var path in new[] { "/register", "/login", "/auth/login/options", "/auth/me", "/Authentication/EnrollmentCode.cs" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    /// <summary>登録失敗でアカウント・パスキー・コードを変更していないことを確認する。</summary>
    private static void AssertUnregistered(EnrollmentApp app)
    {
        Assert.Equal(0L, app.Database.Scalar("SELECT IsEnabled FROM AspNetUsers"));
        Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUserPasskeys"));
        Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM auth_enrollment_codes WHERE ConsumedAtUtc IS NOT NULL"));
    }

    /// <summary>合成EC公開鍵のWebAuthn応答を作り、本番と同じIdentity検証へ渡す。</summary>
    internal static object Credential(JsonElement options, string? corruption = null, byte[]? id = null, ECParameters? publicParameters = null)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var point = publicParameters?.Q ?? key.ExportParameters(false).Q;
        id ??= RandomNumberGenerator.GetBytes(32);
        var challenge = options.GetProperty("challenge").GetString();
        var clientData = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "webauthn.create", challenge = corruption == "challenge" ? "wrong" : challenge,
            origin = corruption == "origin" ? "https://other.example.test" : EnrollmentApp.Origin,
            crossOrigin = corruption == "cross-origin"
        });
        // COSE EC2鍵: kty=2、alg=ES256(-7)、crv=P-256(1)、x/yは各32バイト。
        byte[] publicKey = [0xA5, 0x01, 0x02, 0x03, 0x26, 0x20, 0x01, 0x21, 0x58, 0x20,
            .. point.X!, 0x22, 0x58, 0x20, .. point.Y!];
        byte[] authData = [.. SHA256.HashData(Encoding.UTF8.GetBytes(corruption == "rp" ? "other.example.test" : "monetail.example.test")),
            corruption == "uv" ? (byte)0x41 : (byte)0x45, 0, 0, 0, 0, .. new byte[16], 0, 32, .. id, .. publicKey];
        // CBORマップのfmt=noneは、端末証明書を要求しない標準の登録応答。
        byte[] attestation = [0xA3, 0x63, (byte)'f', (byte)'m', (byte)'t', 0x64, (byte)'n', (byte)'o', (byte)'n', (byte)'e',
            0x67, .. Encoding.ASCII.GetBytes("attStmt"), 0xA0,
            0x68, .. Encoding.ASCII.GetBytes("authData"), 0x58, (byte)authData.Length, .. authData];
        return new
        {
            id = WebEncoders.Base64UrlEncode(id), rawId = WebEncoders.Base64UrlEncode(id), type = "public-key",
            response = new { clientDataJSON = WebEncoders.Base64UrlEncode(clientData),
                attestationObject = WebEncoders.Base64UrlEncode(attestation), transports = new[] { "internal" } },
            clientExtensionResults = new { credProps = new { rk = true } }
        };
    }

    /// <summary>テストだけでUTC時刻を進め、期限境界を待たずに検証する。</summary>
    private sealed class EnrollmentTime : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }

    /// <summary>隔離SQLiteとProductionのHTTP経路を、実際の登録設定で起動する。</summary>
    private sealed class EnrollmentApp : IAsyncDisposable
    {
        public const string Origin = "https://monetail.example.test";
        public AuthDatabaseFixture Database { get; } = new();
        public EnrollmentTime Time { get; } = new();
        public WebApplicationFactory<Program> Factory { get; private set; } = null!;
        public IssuedEnrollmentCode Code { get; private set; } = null!;
        public string ChallengeCookie { get; private set; } = "";

        public static async Task<EnrollmentApp> CreateAsync()
        {
            var app = new EnrollmentApp();
            await app.Database.MigrateAsync();
            app.Factory = app.NewFactory();
            await using var scope = app.Factory.Services.CreateAsyncScope();
            app.Code = (await scope.ServiceProvider.GetRequiredService<AdminProvisioningService>().InitializeAdminAsync(1))!;
            return app;
        }

        public WebApplicationFactory<Program> NewFactory() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production").UseSetting("ConnectionStrings:Monetail", Database.ConnectionString)
                .UseSetting("Passkeys:Origin", Origin).UseSetting("Passkeys:RpId", "monetail.example.test")
                .UseSetting("Passkeys:DataProtectionKeysPath", Path.Combine(Path.GetDirectoryName(Database.DatabasePath)!, "keys"));
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(Time));
        });

        public HttpClient Client(string origin = Origin)
        {
            var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(origin) });
            client.DefaultRequestHeaders.Add("Origin", Origin);
            return client;
        }

        public async Task CsrfAsync(HttpClient client)
        {
            var response = await client.GetFromJsonAsync<JsonElement>("/auth/csrf");
            client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", response.GetProperty("token").GetString());
        }

        public async Task<JsonElement> BeginAsync(HttpClient client)
        {
            await CsrfAsync(client);
            using var response = await client.PostAsJsonAsync("/auth/enrollment/options", new { code = Code.Code });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            ChallengeCookie = response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }

        public Task<HttpResponseMessage> VerifyAsync(HttpClient client, object credential) =>
            client.PostAsJsonAsync("/auth/enrollment/verify", new { credential });

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            Database.Dispose();
        }
    }
}
