using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Authentication;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>人物に依存しない認証・初回表示名・復旧時の名前保持を実HTTPと標準署名で検証する。</summary>
public class AccountIdentityTests
{
    [Theory]
    [InlineData("person-deleted")]
    [InlineData("link-deleted")]
    [InlineData("person-renamed")]
    [InlineData("relinked")]
    public async Task PersonChangesDoNotChangeLoginIdentityNameOrReauthentication(string change)
    {
        await using var app = await LoginTestApp.CreateAsync();
        app.Database.Execute("UPDATE AspNetUsers SET DisplayName='アカウント自身の名前'");
        using var client = app.Client();
        var options = await app.BeginAsync(client);
        switch (change)
        {
            case "person-deleted": app.Database.Execute("DELETE FROM m_user WHERE code=1"); break;
            case "link-deleted": app.Database.Execute("DELETE FROM auth_user_links"); break;
            case "person-renamed": app.Database.Execute("UPDATE m_user SET name='人物だけの変更'"); break;
            case "relinked": app.Database.Execute("UPDATE auth_user_links SET user_code=2"); break;
        }
        using var login = await app.VerifyAsync(client, app.Assertion(options));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var me = await client.GetFromJsonAsync<JsonElement>("/auth/me?userCode=999");
        Assert.Equal(app.UserId, me.GetProperty("authUserId").GetString());
        Assert.Equal("アカウント自身の名前", me.GetProperty("displayName").GetString());
        Assert.False(me.TryGetProperty("userCode", out _));
        var expiry = me.GetProperty("expiresAtUtc").GetDateTimeOffset();
        app.Time.Advance(TimeSpan.FromMinutes(6));
        await LoginTestApp.CsrfAsync(client);
        using var begin = await client.PostAsJsonAsync("/auth/reauthentication/options", new { });
        Assert.Equal(HttpStatusCode.OK, begin.StatusCode);
        options = await begin.Content.ReadFromJsonAsync<JsonElement>();
        using var verified = await client.PostAsJsonAsync("/auth/reauthentication/verify", new { credential = app.Assertion(options) });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        me = await client.GetFromJsonAsync<JsonElement>("/auth/me");
        Assert.Equal(expiry, me.GetProperty("expiresAtUtc").GetDateTimeOffset());
        Assert.Equal("アカウント自身の名前", me.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task MissingPeopleTablesDoNotPreventPasskeyManagementAccountCreationOrRecovery()
    {
        await using var app = await LoginTestApp.CreateAsync();
        app.Database.Execute("ALTER TABLE m_user RENAME TO old_people; ALTER TABLE auth_user_links RENAME TO old_links");
        using var admin = app.Client();
        await app.LoginAsync(admin);
        await LoginTestApp.CsrfAsync(admin);
        using var begin = await admin.PostAsJsonAsync("/auth/passkeys/options", new { });
        Assert.Equal(HttpStatusCode.OK, begin.StatusCode);
        var options = await begin.Content.ReadFromJsonAsync<JsonElement>();
        var key = RandomNumberGenerator.GetBytes(32);
        using var added = await admin.PostAsJsonAsync("/auth/passkeys/verify", new { credential = app.Attestation(options, key) });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var keys = await admin.GetFromJsonAsync<JsonElement>("/auth/passkeys");
        Assert.Equal(2, keys.GetArrayLength());
        using var created = await admin.PostAsJsonAsync("/admin/accounts", new { requestId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var account = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = account.GetProperty("authUserId").GetString()!;
        using var reissued = await admin.PostAsJsonAsync($"/admin/accounts/{id}/enrollment-code", new { });
        Assert.Equal(HttpStatusCode.OK, reissued.StatusCode);
        var rows = await admin.GetFromJsonAsync<JsonElement>("/admin/accounts");
        Assert.Equal(2, rows.GetArrayLength());
        await using (var scope = app.Factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AuthenticationRevocationService>().RevokeAccountAsync(id);
        using var removed = await admin.DeleteAsync($"/auth/passkeys/{Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(key)}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        using var loggedOut = await admin.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, loggedOut.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    [InlineData("too-long")]
    public async Task InvalidInitialNameDoesNotConsumeCodeOrChangeAnyStoredData(string? name)
    {
        await using var app = await LoginTestApp.CreateAsync();
        var issued = await PendingAccountAsync(app);
        using var client = app.Client();
        await LoginTestApp.CsrfAsync(client);
        var before = app.Database.Snapshot(app.Database.Tables());
        using var response = await client.PostAsJsonAsync("/auth/enrollment/options", new
        {
            code = issued.Code, displayName = name == "too-long" ? new string('あ', 101) : name
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitialNameIsStoredOnlyAfterVerificationWithoutLinkingSameNamedPeople(bool maximumLength)
    {
        await using var app = await LoginTestApp.CreateAsync();
        var first = await PendingAccountAsync(app);
        var second = await PendingAccountAsync(app);
        var name = maximumLength ? new string('あ', 100) : "同じ名前";
        foreach (var issued in new[] { first, second })
        {
            using var client = app.Client();
            await LoginTestApp.CsrfAsync(client);
            using var begin = await client.PostAsJsonAsync("/auth/enrollment/options", new { code = issued.Code, displayName = maximumLength ? name : $"  {name}  " });
            Assert.Equal(HttpStatusCode.OK, begin.StatusCode);
            Assert.Equal(1L, app.Database.Scalar($"SELECT COUNT(*) FROM AspNetUsers WHERE Id='{issued.AuthUserId}' AND DisplayName IS NULL"));
            var options = await begin.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(name, options.GetProperty("user").GetProperty("displayName").GetString());
            using var completed = await client.PostAsJsonAsync("/auth/enrollment/verify", new { credential = app.Attestation(options, RandomNumberGenerator.GetBytes(32)) });
            Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
            Assert.Equal(name, app.Database.Scalar($"SELECT DisplayName FROM AspNetUsers WHERE Id='{issued.AuthUserId}'"));
            Assert.Equal(0L, app.Database.Scalar($"SELECT COUNT(*) FROM auth_user_links WHERE auth_user_id='{issued.AuthUserId}'"));
            using var anonymous = await client.GetAsync("/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }
    }

    [Fact]
    public async Task RecoveryKeepsTheExistingAccountNameDespiteAReplacementNameInTheRequest()
    {
        await using var app = await LoginTestApp.CreateAsync();
        var issued = await PendingAccountAsync(app);
        using var client = app.Client();
        await LoginTestApp.CsrfAsync(client);
        using var begin = await client.PostAsJsonAsync("/auth/enrollment/options", new { code = issued.Code, displayName = "本人が決めた名前" });
        var options = await begin.Content.ReadFromJsonAsync<JsonElement>();
        using var enrolled = await client.PostAsJsonAsync("/auth/enrollment/verify", new { credential = app.Attestation(options, RandomNumberGenerator.GetBytes(32)) });
        Assert.Equal(HttpStatusCode.OK, enrolled.StatusCode);
        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AuthenticationRevocationService>().RevokeAccountAsync(issued.AuthUserId);
            issued = await scope.ServiceProvider.GetRequiredService<AdminProvisioningService>().IssueEnrollmentCodeAsync(issued.AuthUserId);
        }
        using var recovery = await client.PostAsJsonAsync("/auth/enrollment/options", new { code = issued.Code, displayName = "置き換えを要求した名前" });
        Assert.Equal(HttpStatusCode.OK, recovery.StatusCode);
        options = await recovery.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("本人が決めた名前", options.GetProperty("user").GetProperty("displayName").GetString());
        using var completed = await client.PostAsJsonAsync("/auth/enrollment/verify", new { credential = app.Attestation(options, RandomNumberGenerator.GetBytes(32)) });
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.Equal("本人が決めた名前", app.Database.Scalar($"SELECT DisplayName FROM AspNetUsers WHERE Id='{issued.AuthUserId}'"));
    }

    /// <summary>人物との対応を作らず、初回登録待ちのアカウントと有効な登録コードを用意する。</summary>
    private static async Task<IssuedEnrollmentCode> PendingAccountAsync(LoginTestApp app)
    {
        await using var scope = app.Factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AuthUser>>();
        var user = new AuthUser { UserName = Guid.NewGuid().ToString("N") };
        Assert.True((await users.CreateAsync(user)).Succeeded);
        return await scope.ServiceProvider.GetRequiredService<AdminProvisioningService>().IssueEnrollmentCodeAsync(user.Id);
    }
}
