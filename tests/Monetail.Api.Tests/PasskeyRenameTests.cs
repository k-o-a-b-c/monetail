using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Authentication;
using Monetail.Api.Persistence;
using Xunit;

namespace Monetail.Api.Tests;

public sealed class PasskeyRenameTests
{
    [Fact]
    public async Task RenameChangesOnlyOwnNameAndPreservesLoginWithTheSameKey()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client(); await app.LoginAsync(client);
        var keyBefore = await KeyAsync(app);
        var before = app.Database.Snapshot(app.Database.Tables().Except(["AspNetUserPasskeys"]).ToArray());
        var accountBefore = app.Database.Scalar("SELECT SecurityStamp FROM AspNetUsers");
        await LoginTestApp.CsrfAsync(client);
        using var renamed = await RenameAsync(client, app.CredentialId, "  自宅のパスキー  ");
        Assert.Equal(HttpStatusCode.NoContent, renamed.StatusCode);
        Assert.False(renamed.Headers.Contains("Set-Cookie"));
        var keyAfter = await KeyAsync(app); Assert.Equal("自宅のパスキー", keyAfter.Name);
        keyAfter.Name = keyBefore.Name;
        Assert.Equal(JsonSerializer.Serialize(keyBefore), JsonSerializer.Serialize(keyAfter));
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables().Except(["AspNetUserPasskeys"]).ToArray()));
        Assert.Equal(accountBefore, app.Database.Scalar("SELECT SecurityStamp FROM AspNetUsers"));
        using var me = await client.GetAsync("/auth/me"); Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var next = app.Client(); var options = await app.BeginAsync(next);
        using var signedIn = await app.VerifyAsync(next, app.Assertion(options));
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        Assert.Equal("自宅のパスキー", (await KeyAsync(app)).Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t\r\n ")]
    [InlineData(null)]
    public async Task EmptyNamesAreRejectedWithoutWrites(string? name) => await InvalidAsync(new { name });

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"name\":1}")]
    [InlineData("{\"name\":\"ok\",\"id\":\"other\"}")]
    [InlineData("{\"name\":\"a\",\"name\":\"b\"}")]
    [InlineData("[]")]
    public async Task InvalidJsonShapeIsRejectedWithoutWrites(string json)
    {
        await using var app = await LoginTestApp.CreateAsync(); using var client = app.Client(); await app.LoginAsync(client);
        await LoginTestApp.CsrfAsync(client); var before = app.Database.Snapshot(app.Database.Tables());
        using var response = await client.PatchAsync(Path(app.CredentialId), new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task LengthUsesTrimmedUtf16AndDuplicatesAreAllowed()
    {
        await using var app = await LoginTestApp.CreateAsync(); using var client = app.Client(); await app.LoginAsync(client);
        await LoginTestApp.CsrfAsync(client);
        using var tooLong = await RenameAsync(client, app.CredentialId, new string('あ', 101)); Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        using var surrogate = await RenameAsync(client, app.CredentialId, string.Concat(Enumerable.Repeat("😀", 51))); Assert.Equal(HttpStatusCode.BadRequest, surrogate.StatusCode);
        var name = new string('あ', 100);
        using var edge = await RenameAsync(client, app.CredentialId, "  " + name + "  "); Assert.Equal(HttpStatusCode.NoContent, edge.StatusCode);
        var options = await client.PostAsJsonAsync("/auth/passkeys/options", new { });
        var creation = await options.Content.ReadFromJsonAsync<JsonElement>(); var id = RandomNumberGenerator.GetBytes(32);
        using var added = await client.PostAsJsonAsync("/auth/passkeys/verify", new { credential = app.Attestation(creation, id) }); Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        using var duplicate = await RenameAsync(client, id, name); Assert.Equal(HttpStatusCode.NoContent, duplicate.StatusCode);
        var before = app.Database.Snapshot(app.Database.Tables());
        using var unchanged = await RenameAsync(client, id, name); Assert.Equal(HttpStatusCode.NoContent, unchanged.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Theory]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("expired-verification", HttpStatusCode.Forbidden)]
    [InlineData("boundary", HttpStatusCode.Forbidden)]
    [InlineData("csrf", HttpStatusCode.BadRequest)]
    [InlineData("origin", HttpStatusCode.Forbidden)]
    [InlineData("foreign", HttpStatusCode.NotFound)]
    [InlineData("missing", HttpStatusCode.NotFound)]
    [InlineData("revoked", HttpStatusCode.Unauthorized)]
    public async Task SecurityBoundariesRejectWithoutChangingDatabase(string mode, HttpStatusCode expected)
    {
        await using var app = await LoginTestApp.CreateAsync(); using var client = app.Client();
        var id = app.CredentialId;
        if (mode == "foreign") id = (await app.AddAccountAsync()).CredentialId;
        if (mode != "anonymous") { await app.LoginAsync(client); await LoginTestApp.CsrfAsync(client); }
        if (mode == "expired-verification") app.Time.Advance(TimeSpan.FromMinutes(6));
        if (mode == "boundary") app.Time.Advance(TimeSpan.FromMinutes(5));
        if (mode == "csrf") client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        if (mode == "origin") { client.DefaultRequestHeaders.Remove("Origin"); client.DefaultRequestHeaders.Add("Origin", "https://other.example.test"); }
        if (mode == "missing") id = [1, 2, 3];
        if (mode == "revoked") app.Database.Execute("UPDATE auth_sessions SET RevokedAtUtc=VerifiedAtUtc");
        var before = app.Database.Snapshot(app.Database.Tables());
        using var response = await RenameAsync(client, id, "変更禁止");
        Assert.Equal(expected, response.StatusCode); Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task SaveFailureRollsBackAndRenameSharesTheExistingAuthenticationLimit()
    {
        await using var app = await LoginTestApp.CreateAsync(); using var client = app.Client(); await app.LoginAsync(client); await LoginTestApp.CsrfAsync(client);
        app.Database.Execute("CREATE TRIGGER fail_name BEFORE UPDATE ON AspNetUserPasskeys BEGIN SELECT RAISE(ABORT,'injected'); END");
        var before = app.Database.Snapshot(app.Database.Tables());
        using var failed = await RenameAsync(client, app.CredentialId, "変更"); Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables())); app.Database.Execute("DROP TRIGGER fail_name");
        // ログインの2回と失敗の1回を含む既存20回/IP枠を共有する。
        for (var i = 0; i < 17; i++) { using var result = await RenameAsync(client, app.CredentialId, "変更"); Assert.Equal(HttpStatusCode.NoContent, result.StatusCode); }
        using var limited = await RenameAsync(client, app.CredentialId, "変更"); Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Theory]
    [InlineData("UPDATE auth_sessions SET RevokedAtUtc=VerifiedAtUtc")]
    [InlineData("UPDATE auth_sessions SET VerifiedAtUtc='2000-01-01 00:00:00'")]
    [InlineData("UPDATE AspNetUsers SET IsEnabled=0")]
    public async Task AuthenticationChangedDuringSaveRollsBackEverything(string mutation)
    {
        await using var app = await LoginTestApp.CreateAsync(); using var client = app.Client(); await app.LoginAsync(client); await LoginTestApp.CsrfAsync(client);
        app.Database.Execute("CREATE TRIGGER change_auth AFTER UPDATE ON AspNetUserPasskeys BEGIN " + mutation + "; END");
        var before = app.Database.Snapshot(app.Database.Tables());
        using var response = await RenameAsync(client, app.CredentialId, "保存しない");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task RenameAndRealAssertionDoNotOverwriteEachOthersNameOrSignatureCounter()
    {
        await using var app = await LoginTestApp.CreateAsync(); using var client = app.Client(); await app.LoginAsync(client, count: 1); await LoginTestApp.CsrfAsync(client);
        using var next = app.Client(); var challenge = await app.BeginAsync(next);
        var rename = RenameAsync(client, app.CredentialId, "同時変更");
        var login = app.VerifyAsync(next, app.Assertion(challenge, count: 2));
        await Task.WhenAll(rename, login);
        using var renamed = await rename; using var loggedIn = await login;
        Assert.Equal(HttpStatusCode.NoContent, renamed.StatusCode); Assert.Equal(HttpStatusCode.OK, loggedIn.StatusCode);
        var key = await KeyAsync(app); Assert.Equal("同時変更", key.Name); Assert.Equal(2u, key.SignCount);
    }

    private static async Task InvalidAsync(object body)
    {
        await using var app = await LoginTestApp.CreateAsync(); using var client = app.Client(); await app.LoginAsync(client); await LoginTestApp.CsrfAsync(client);
        var before = app.Database.Snapshot(app.Database.Tables()); using var response = await client.PatchAsJsonAsync(Path(app.CredentialId), body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }
    private static string Path(byte[] id) => "/auth/passkeys/" + WebEncoders.Base64UrlEncode(id);
    private static Task<HttpResponseMessage> RenameAsync(HttpClient client, byte[] id, string name) => client.PatchAsJsonAsync(Path(id), new { name });
    private static async Task<UserPasskeyInfo> KeyAsync(LoginTestApp app)
    {
        await using var scope = app.Factory.Services.CreateAsyncScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<AuthUser>>();
        return Assert.Single(await users.GetPasskeysAsync((await users.FindByIdAsync(app.UserId))!));
    }
}
