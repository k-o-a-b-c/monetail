using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Authentication;
using Xunit;

namespace Monetail.Api.Tests;

public sealed class PasskeyNamingFlowTests
{
    private static readonly Guid Provider = new("ea9b8d66-4d01-1d21-3ce4-b6b48cb575d4");
    [Fact]
    public async Task PublicRegistrationUsesTheCommonVerifiedNamingWithoutSigningIn()
    {
        await using var app = await SelfRegistrationTestApp.CreateAsync(); using var client = app.Client();
        var options = await SelfRegistrationTestApp.BeginAsync(client);
        using var response = await SelfRegistrationTestApp.VerifyAsync(client, PasskeyEnrollmentTests.Credential(options, aaguid: Provider));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = app.Factory.Services.CreateAsyncScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<AuthUser>>();
        var user = Assert.Single(users.Users);
        Assert.Equal("Google パスワード マネージャー", Assert.Single(await users.GetPasskeysAsync(user)).Name);
        using var me = await client.GetAsync("/auth/me"); Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdditionAndRecoveryUseTheCommonNameAndNeverRenameExistingKeys(bool recovery)
    {
        await using var app = await LoginTestApp.CreateAsync(); using var client = app.Client();
        string root;
        JsonElement options;
        if (recovery)
        {
            await using var scope = app.Factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AuthenticationRevocationService>().RevokeAccountAsync(app.UserId, emergencyRecovery: true);
            var link = (await scope.ServiceProvider.GetRequiredService<AdminProvisioningService>().IssueRecoveryLinkAsync(app.UserId))!;
            root = "/auth/enrollment"; await LoginTestApp.CsrfAsync(client);
            using var begin = await client.PostAsJsonAsync(root + "/options", new { token = link.Token(), displayName = "変更しない名前" });
            Assert.Equal(HttpStatusCode.OK, begin.StatusCode); options = await begin.Content.ReadFromJsonAsync<JsonElement>();
        }
        else
        {
            await app.LoginAsync(client); root = "/auth/passkeys"; await LoginTestApp.CsrfAsync(client);
            using var begin = await client.PostAsJsonAsync(root + "/options", new { });
            Assert.Equal(HttpStatusCode.OK, begin.StatusCode); options = await begin.Content.ReadFromJsonAsync<JsonElement>();
        }
        using var complete = await client.PostAsJsonAsync(root + "/verify", new { credential = app.Attestation(options, RandomNumberGenerator.GetBytes(32), aaguid: Provider) });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        await using var read = app.Factory.Services.CreateAsyncScope(); var users = read.ServiceProvider.GetRequiredService<UserManager<AuthUser>>();
        var keys = await users.GetPasskeysAsync((await users.FindByIdAsync(app.UserId))!);
        Assert.Single(keys, k => k.Name == "Google パスワード マネージャー");
        if (recovery) { Assert.Single(keys); using var me = await client.GetAsync("/auth/me"); Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode); }
        else { Assert.Equal(2, keys.Count); Assert.Single(keys, k => k.Name == "synthetic-login-key"); }
    }
    [Fact]
    public async Task OptionalNamingParseFailureDoesNotFailValidRegistration()
    {
        await using var app = await LoginTestApp.CreateAsync(); using var client = app.Client(); await app.LoginAsync(client);
        await using var factory = app.NewFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddScoped<PasskeyHandler<AuthUser>>();
            services.AddScoped<IPasskeyHandler<AuthUser>>(provider => new LostOptionalMetadata(provider.GetRequiredService<PasskeyHandler<AuthUser>>()));
        }));
        using var current = app.Client(factory); current.DefaultRequestHeaders.Add("Cookie", app.SessionCookie.Split(';')[0]); await LoginTestApp.CsrfAsync(current);
        using var begin = await current.PostAsJsonAsync("/auth/passkeys/options", new { });
        var options = await begin.Content.ReadFromJsonAsync<JsonElement>();
        using var complete = await current.PostAsJsonAsync("/auth/passkeys/verify", new { credential = app.Attestation(options, RandomNumberGenerator.GetBytes(32), aaguid: Provider) });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        await using var scope = app.Factory.Services.CreateAsyncScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<AuthUser>>();
        Assert.Single(await users.GetPasskeysAsync((await users.FindByIdAsync(app.UserId))!), k => k.Name == "パスキー");
    }

    // 検証自体は実Identityに任せ、成功後の任意表示用メタデータだけを欠損させる。
    private sealed class LostOptionalMetadata(IPasskeyHandler<AuthUser> inner) : IPasskeyHandler<AuthUser>
    {
        public Task<PasskeyCreationOptionsResult> MakeCreationOptionsAsync(PasskeyUserEntity user, Microsoft.AspNetCore.Http.HttpContext http) => inner.MakeCreationOptionsAsync(user, http);
        public Task<PasskeyRequestOptionsResult> MakeRequestOptionsAsync(AuthUser? user, Microsoft.AspNetCore.Http.HttpContext http) => inner.MakeRequestOptionsAsync(user, http);
        public Task<PasskeyAssertionResult<AuthUser>> PerformAssertionAsync(PasskeyAssertionContext context) => inner.PerformAssertionAsync(context);
        public async Task<PasskeyAttestationResult> PerformAttestationAsync(PasskeyAttestationContext context)
        {
            var result = await inner.PerformAttestationAsync(context); if (!result.Succeeded) return result;
            var k = result.Passkey;
            return PasskeyAttestationResult.Success(new(k.CredentialId, k.PublicKey, k.CreatedAt, k.SignCount, k.Transports,
                k.IsUserVerified, k.IsBackupEligible, k.IsBackedUp, [0xff], k.ClientDataJson), result.UserEntity);
        }
    }
}
