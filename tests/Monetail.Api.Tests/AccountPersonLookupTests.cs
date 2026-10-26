using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Authentication;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>人物照合の最新状態・同一トランザクションと、認証からの差替え境界を検証する。</summary>
public class AccountPersonLookupTests
{
    [Fact]
    public async Task LookupUsesOnlyTheCurrentLinkAndPersonAndDoesNotTrackOrWriteData()
    {
        await using var app = await LoginTestApp.CreateAsync();
        var other = await app.AddAccountAsync(2);
        await using var scope = app.Factory.Services.CreateAsyncScope();
        var people = scope.ServiceProvider.GetRequiredService<IAccountPersonLookup>();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var before = app.Database.Snapshot(app.Database.Tables());
        Assert.Equal(new AccountPerson(1, "同じ名前"), await people.FindAsync(app.UserId, default));
        Assert.Equal(new AccountPerson(2, "同じ名前"), await people.FindAsync(other.UserId, default));
        Assert.Null(await people.FindAsync("same-account", default));
        Assert.Null(await people.FindAsync("' OR 1=1 --", default));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        app.Database.Execute("UPDATE m_user SET name='更新''名' WHERE code=2");
        Assert.Equal(new AccountPerson(2, "更新'名"), await people.FindAsync(other.UserId, default));
        app.Database.Execute("UPDATE auth_user_links SET user_code=3 WHERE user_code=2");
        Assert.Equal(new AccountPerson(3, "名前だけ"), await people.FindAsync(other.UserId, default));
        app.Database.Execute("DELETE FROM m_user WHERE code=3");
        before = app.Database.Snapshot(app.Database.Tables());
        Assert.Null(await people.FindAsync(other.UserId, default));
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        app.Database.Execute("DELETE FROM auth_user_links WHERE user_code=3");
        before = app.Database.Snapshot(app.Database.Tables());
        Assert.Null(await people.FindAsync(other.UserId, default));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task LookupReadsUncommittedChangesAndRollbackInTheCallingDatabaseScope()
    {
        await using var app = await LoginTestApp.CreateAsync();
        await using var scope = app.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var people = scope.ServiceProvider.GetRequiredService<IAccountPersonLookup>();
        var before = app.Database.Snapshot(app.Database.Tables());
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE auth_user_links SET user_code=3 WHERE auth_user_id={app.UserId}");
            await context.Database.ExecuteSqlRawAsync("UPDATE m_user SET name='未確定の名前' WHERE code=3");
            Assert.Equal(new AccountPerson(3, "未確定の名前"), await people.FindAsync(app.UserId, default));
            await transaction.RollbackAsync();
        }
        Assert.Equal(new AccountPerson(1, "同じ名前"), await people.FindAsync(app.UserId, default));
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task ReplacementLookupIsUsedByLoginCookieReauthenticationAndPasskeyManagement()
    {
        await using var app = await LoginTestApp.CreateAsync();
        await using var factory = app.NewFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddScoped<IAccountPersonLookup>(_ => new ReplacementLookup(app.UserId))));
        using var client = app.Client(factory);
        await app.LoginAsync(client);
        var me = await client.GetFromJsonAsync<JsonElement>("/auth/me");
        Assert.Equal(901, me.GetProperty("userCode").GetInt32());
        Assert.Equal("差替え先の表示名", me.GetProperty("displayName").GetString());
        await LoginTestApp.CsrfAsync(client);
        using var reauthentication = await client.PostAsJsonAsync("/auth/reauthentication/options", new { });
        var options = await reauthentication.Content.ReadFromJsonAsync<JsonElement>();
        using var verified = await client.PostAsJsonAsync("/auth/reauthentication/verify", new { credential = app.Assertion(options) });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        using var addition = await client.PostAsJsonAsync("/auth/passkeys/options", new { });
        Assert.Equal(HttpStatusCode.OK, addition.StatusCode);
        options = await addition.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("差替え先の表示名", options.GetProperty("user").GetProperty("displayName").GetString());
        var id = RandomNumberGenerator.GetBytes(32);
        using var added = await client.PostAsJsonAsync("/auth/passkeys/verify", new { credential = app.Attestation(options, id) });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        using var deleted = await client.DeleteAsync("/auth/passkeys/" + WebEncoders.Base64UrlEncode(id));
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var loggedOut = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, loggedOut.StatusCode);
        Assert.Equal(1L, app.Database.Scalar("SELECT user_code FROM auth_user_links"));
        Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM m_user WHERE code=901"));
        Assert.Equal(777L, app.Database.Scalar("SELECT total FROM t_receipts WHERE seq=2"));
    }

    [Fact]
    public async Task EnrollmentUsesTheReplacementPersonAndNameWithoutChangingStoredPersonLinks()
    {
        await using var app = await LoginTestApp.CreateAsync();
        IssuedEnrollmentCode issued;
        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AuthUser>>();
            var user = new AuthUser { UserName = Guid.NewGuid().ToString("N") };
            Assert.True((await users.CreateAsync(user)).Succeeded);
            var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            context.UserLinks.Add(new AuthUserLink { AuthUserId = user.Id, UserCode = 2 });
            await context.SaveChangesAsync();
            issued = await scope.ServiceProvider.GetRequiredService<AdminProvisioningService>().IssueEnrollmentCodeAsync(2);
        }
        await using var factory = app.NewFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddScoped<IAccountPersonLookup>(_ => new ReplacementLookup(issued.AuthUserId))));
        using var client = app.Client(factory);
        await LoginTestApp.CsrfAsync(client);
        using var begin = await client.PostAsJsonAsync("/auth/enrollment/options", new { code = issued.Code });
        Assert.Equal(HttpStatusCode.OK, begin.StatusCode);
        var options = await begin.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("差替え先の表示名", options.GetProperty("user").GetProperty("displayName").GetString());
        using var complete = await client.PostAsJsonAsync("/auth/enrollment/verify",
            new { credential = app.Attestation(options, RandomNumberGenerator.GetBytes(32)) });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        Assert.Equal(1L, app.Database.Scalar($"SELECT IsEnabled FROM AspNetUsers WHERE Id='{issued.AuthUserId}'"));
        Assert.Equal(2L, app.Database.Scalar($"SELECT user_code FROM auth_user_links WHERE auth_user_id='{issued.AuthUserId}'"));
        Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM auth_sessions"));
    }

    /// <summary>Monetailの人物マスタに存在しない人物情報へ差し替え、認証の委譲を確認する。</summary>
    private sealed class ReplacementLookup(string authUserId) : IAccountPersonLookup
    {
        /// <summary>指定アカウントだけに、テスト用の人物情報を返す。</summary>
        public Task<AccountPerson?> FindAsync(string id, CancellationToken token) =>
            Task.FromResult(id == authUserId ? new AccountPerson(901, "差替え先の表示名") : null);
    }
}
