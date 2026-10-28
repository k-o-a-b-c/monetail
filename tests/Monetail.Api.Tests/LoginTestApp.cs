using System.Net.Http.Json;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Authentication;
using Monetail.Api.Persistence;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>実Identityが検証するEC署名と、隔離DBのログインHTTP経路を用意する。</summary>
internal sealed class LoginTestApp : IAsyncDisposable
{
    public const string Origin = "https://monetail.example.test";
    public AuthDatabaseFixture Database { get; } = new();
    public LoginTime Time { get; } = new();
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public string UserId { get; private set; } = "";
    public string ChallengeCookie { get; private set; } = "";
    public string SessionCookie { get; private set; } = "";
    public byte[] CredentialId { get; } = RandomNumberGenerator.GetBytes(32);
    private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private bool desktopOAuth;

    /// <summary>有効アカウントとテスト公開鍵を用意する。</summary>
    public static async Task<LoginTestApp> CreateAsync(bool desktopOAuth = false)
    {
        var app = new LoginTestApp();
        await app.Database.MigrateAsync();
        app.desktopOAuth = desktopOAuth;
        if (desktopOAuth) app.CreateOAuthCertificates();
        app.Factory = app.NewFactory();
        await using var scope = app.Factory.Services.CreateAsyncScope();
        var issued = (await scope.ServiceProvider.GetRequiredService<AdminProvisioningService>().InitializeAdminAsync())!;
        app.UserId = issued.AuthUserId;
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AuthUser>>();
        var user = (await users.FindByIdAsync(app.UserId))!;
        var point = app.key.ExportParameters(false).Q;
        byte[] publicKey = [0xA5, 0x01, 0x02, 0x03, 0x26, 0x20, 0x01, 0x21, 0x58, 0x20,
            .. point.X!, 0x22, 0x58, 0x20, .. point.Y!];
        var passkey = new UserPasskeyInfo(app.CredentialId, publicKey, app.Time.GetUtcNow(), 0,
            ["internal"], true, false, false, [], []) { Name = "synthetic-login-key" };
        Assert.True((await users.AddOrUpdatePasskeyAsync(user, passkey)).Succeeded);
        user.IsEnabled = true;
        user.DisplayName = "同じ名前";
        Assert.True((await users.UpdateAsync(user)).Succeeded);
        var context = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        await context.SaveChangesAsync();
        return app;
    }

    /// <summary>同じ永続キーとDBを使い、プロセス再起動相当のホストを構築する。</summary>
    public WebApplicationFactory<Program> NewFactory() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Production").UseSetting("ConnectionStrings:Monetail", Database.ConnectionString)
            .UseSetting("Passkeys:Origin", Origin).UseSetting("Passkeys:RpId", "monetail.example.test")
            .UseSetting("Passkeys:DataProtectionKeysPath", Path.Combine(Path.GetDirectoryName(Database.DatabasePath)!, "keys"));
        if (desktopOAuth)
        {
            builder.UseSetting("DesktopOAuth:SigningCertificatePath", Path.Combine(Path.GetDirectoryName(Database.DatabasePath)!, "signing.pfx"))
                .UseSetting("DesktopOAuth:EncryptionCertificatePath", Path.Combine(Path.GetDirectoryName(Database.DatabasePath)!, "encryption.pfx"));
        }
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(Time);
            services.AddSingleton<IStartupFilter, ProtectedProbe>();
        });
    });

    /// <summary>隔離した一時フォルダへ、テスト専用の署名・暗号化証明書を生成する。</summary>
    private void CreateOAuthCertificates()
    {
        foreach (var name in new[] { "signing", "encryption" })
        {
            using var algorithm = RSA.Create(2048);
            var request = new CertificateRequest("CN=synthetic-" + name, algorithm, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509KeyUsageExtension(name == "signing"
                ? X509KeyUsageFlags.DigitalSignature : X509KeyUsageFlags.KeyEncipherment, true));
            using var certificate = request.CreateSelfSigned(Time.GetUtcNow().AddYears(-1), Time.GetUtcNow().AddYears(2));
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Database.DatabasePath)!, name + ".pfx"), certificate.Export(X509ContentType.Pfx));
        }
    }

    /// <summary>Cookieを他ブラウザーと共有しないHTTPSクライアントを作る。</summary>
    public HttpClient Client(WebApplicationFactory<Program>? factory = null)
    {
        var client = (factory ?? Factory).CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(Origin), AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("Origin", Origin);
        return client;
    }

    /// <summary>現在の匿名・認証状態に対応するCSRFトークンを取り直す。</summary>
    public static async Task CsrfAsync(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<JsonElement>("/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", response.GetProperty("token").GetString());
    }

    /// <summary>ログイン専用のチャレンジと、名前入力なしの作成条件を取得する。</summary>
    public async Task<JsonElement> BeginAsync(HttpClient client, string? timeZone = "Asia/Tokyo")
    {
        await CsrfAsync(client);
        using var response = await client.PostAsJsonAsync("/auth/login/options", new { timeZone });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ChallengeCookie = response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>実際のEC秘密鍵でWebAuthn認証応答に署名し、必要時に検証項目を壊す。</summary>
    public object Assertion(JsonElement options, string? corruption = null, uint count = 0)
        => AssertionFor(options, UserId, CredentialId, corruption, count);

    /// <summary>指定アカウントの資格情報で署名し、別アカウントの再確認拒否も検証する。</summary>
    public object AssertionFor(JsonElement options, string userId, byte[] credentialId, string? corruption = null, uint count = 0)
    {
        var clientData = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "webauthn.get", challenge = corruption == "challenge" ? "wrong" : options.GetProperty("challenge").GetString(),
            origin = corruption == "origin" ? "https://other.example.test" : Origin,
            crossOrigin = corruption == "cross-origin"
        });
        byte[] authData = [.. SHA256.HashData(Encoding.UTF8.GetBytes(corruption == "rp" ? "other.example.test" : "monetail.example.test")),
            corruption == "uv" ? (byte)0x01 : (byte)0x05, (byte)(count >> 24), (byte)(count >> 16), (byte)(count >> 8), (byte)count];
        var signature = key.SignData([.. authData, .. SHA256.HashData(clientData)], HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);
        if (corruption == "signature") signature[^1] ^= 1;
        var id = corruption == "credential" ? RandomNumberGenerator.GetBytes(32) : credentialId;
        return new
        {
            id = WebEncoders.Base64UrlEncode(id), rawId = WebEncoders.Base64UrlEncode(id), type = "public-key",
            response = new
            {
                clientDataJSON = WebEncoders.Base64UrlEncode(clientData), authenticatorData = WebEncoders.Base64UrlEncode(authData),
                signature = WebEncoders.Base64UrlEncode(signature), userHandle = corruption == "user" ? "b3RoZXI" : WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(userId))
            },
            clientExtensionResults = new { }
        };
    }

    public Task<HttpResponseMessage> VerifyAsync(HttpClient client, object credential) =>
        client.PostAsJsonAsync("/auth/login/verify", new { credential });

    /// <summary>ログイン署名と同じテスト鍵で、標準のパスキー追加応答を作る。</summary>
    public object Attestation(JsonElement options, byte[] id, string? corruption = null) =>
        PasskeyEnrollmentTests.Credential(options, corruption, id, key.ExportParameters(false));

    /// <summary>同じ表示名の別アカウントと資格情報を作成する。</summary>
    public async Task<(string UserId, byte[] CredentialId)> AddAccountAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AuthUser>>();
        var user = new AuthUser { UserName = Guid.NewGuid().ToString("N"), IsEnabled = true, DisplayName = "同じ名前" };
        Assert.True((await users.CreateAsync(user)).Succeeded);
        var point = key.ExportParameters(false).Q;
        byte[] publicKey = [0xA5, 0x01, 0x02, 0x03, 0x26, 0x20, 0x01, 0x21, 0x58, 0x20,
            .. point.X!, 0x22, 0x58, 0x20, .. point.Y!];
        var id = RandomNumberGenerator.GetBytes(32);
        Assert.True((await users.AddOrUpdatePasskeyAsync(user, new UserPasskeyInfo(id, publicKey,
            Time.GetUtcNow(), 0, ["internal"], true, false, false, [], []))).Succeeded);
        var context = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        await context.SaveChangesAsync();
        return (user.Id, id);
    }

    /// <summary>ログインに成功し、再起動や再送検証用に保護Cookieを記録する。</summary>
    public async Task LoginAsync(HttpClient client, string? zone = "Asia/Tokyo", uint count = 0)
    {
        var options = await BeginAsync(client, zone);
        using var response = await VerifyAsync(client, Assertion(options, count: count));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SessionCookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith(SessionCookieEvents.CookieName + "=", StringComparison.Ordinal));
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        key.Dispose();
        Database.Dispose();
    }

    /// <summary>実際の未来から開始し、ブラウザーCookieを消さずに期限境界を進める。</summary>
    internal sealed class LoginTime : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow.AddMinutes(1);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
        public void Set(DateTimeOffset value) => now = value;
    }

    /// <summary>テスト専用経路で、既定の認証必須と最新Adminロールの判定を確認する。</summary>
    private sealed class ProtectedProbe : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/auth/test-default", () => "protected");
                endpoints.MapGet("/auth/test-admin", () => "admin").RequireAuthorization(policy => policy.RequireRole("Admin"));
                endpoints.MapGet("/auth/test-recent", () => "recent").RequireAuthorization(RecentPasskeyRequirement.PolicyName);
                endpoints.MapGet("/auth/test-recent-admin", () => "recent-admin")
                    .RequireAuthorization(RecentPasskeyRequirement.PolicyName).RequireAuthorization(policy => policy.RequireRole("Admin"));
            });
        };
    }
}
