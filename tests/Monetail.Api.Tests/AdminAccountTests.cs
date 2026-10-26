using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Authentication;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>管理者の人物・アカウント一覧と、認証・権限・参照時の保存保持を検証する。</summary>
public class AdminAccountTests
{
    [Fact]
    public async Task ListIncludesNameOnlyPeopleAndOnlyPublicAccountStateWithoutChangingStoredData()
    {
        await using var app = await LoginTestApp.CreateAsync();
        string pendingId;
        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AuthUser>>();
            var pending = new AuthUser { UserName = Guid.NewGuid().ToString("N"), IsEnabled = false };
            Assert.True((await users.CreateAsync(pending)).Succeeded);
            pendingId = pending.Id;
            var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            context.UserLinks.Add(new AuthUserLink { AuthUserId = pending.Id, UserCode = 2 });
            await context.SaveChangesAsync();
        }
        var stopped = await app.AddAccountAsync(4);
        app.Database.Execute($"UPDATE AspNetUsers SET IsEnabled=0 WHERE Id='{stopped.UserId}'");
        using var client = app.Client();
        await app.LoginAsync(client);
        app.Time.Advance(TimeSpan.FromMinutes(6));
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Remove("Origin");
        var before = app.Database.Snapshot(app.Database.Tables());
        using var response = await client.GetAsync("/admin/accounts");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var list = await response.Content.ReadFromJsonAsync<JsonElement>();
        var rows = list.EnumerateArray().ToArray();
        Assert.Equal(new[] { 1, 2, 3, 4 }, rows.Select(row => row.GetProperty("userCode").GetInt32()).ToArray());
        Assert.Equal("同じ名前", rows[0].GetProperty("displayName").GetString());
        Assert.Equal("同じ名前", rows[1].GetProperty("displayName").GetString());
        AssertAccount(rows[0], app.UserId, true);
        AssertAccount(rows[1], pendingId, false);
        Assert.Equal("名前だけ", rows[2].GetProperty("displayName").GetString());
        Assert.Equal(JsonValueKind.Null, rows[2].GetProperty("account").ValueKind);
        AssertAccount(rows[3], stopped.UserId, false);
        foreach (var row in rows)
            Assert.Equal(new[] { "account", "displayName", "userCode" }, row.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task AnonymousAndOrdinaryAccountsCannotReadAnyPeople()
    {
        await using var app = await LoginTestApp.CreateAsync();
        var other = await app.AddAccountAsync(2);
        using var anonymous = app.Client();
        using var denied = await anonymous.GetAsync("/admin/accounts");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Null(denied.Headers.Location);
        Assert.Equal("no-store", denied.Headers.CacheControl!.ToString());
        Assert.Empty(await denied.Content.ReadAsStringAsync());
        using var ordinary = app.Client();
        var options = await app.BeginAsync(ordinary);
        using var login = await app.VerifyAsync(ordinary, app.AssertionFor(options, other.UserId, other.CredentialId));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var before = app.Database.Snapshot(app.Database.Tables());
        using var forbidden = await ordinary.GetAsync("/admin/accounts?role=Admin&userCode=1");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Null(forbidden.Headers.Location);
        Assert.Empty(await forbidden.Content.ReadAsStringAsync());
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task RoleRemovalAndRestorationApplyImmediatelyToTheSameLoginCookie()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        await app.LoginAsync(client);
        using var allowed = await client.GetAsync("/admin/accounts");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AuthUser>>();
            var user = (await users.FindByIdAsync(app.UserId))!;
            Assert.True((await users.RemoveFromRoleAsync(user, "Admin")).Succeeded);
        }
        var before = app.Database.Snapshot(app.Database.Tables());
        using var denied = await client.GetAsync("/admin/accounts");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.False(denied.Headers.Contains("Set-Cookie"));
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AuthUser>>();
            var user = (await users.FindByIdAsync(app.UserId))!;
            Assert.True((await users.AddToRoleAsync(user, "Admin")).Succeeded);
        }
        using var restored = await client.GetAsync("/admin/accounts");
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.False(restored.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData("disabled", HttpStatusCode.Unauthorized)]
    [InlineData("revoked", HttpStatusCode.Unauthorized)]
    [InlineData("expired", HttpStatusCode.Unauthorized)]
    [InlineData("person", HttpStatusCode.Forbidden)]
    [InlineData("link", HttpStatusCode.Forbidden)]
    [InlineData("store", HttpStatusCode.ServiceUnavailable)]
    public async Task LiveSessionAndPersonFailuresRefuseTheListWithoutWritingData(string change, HttpStatusCode expected)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        await app.LoginAsync(client);
        switch (change)
        {
            case "disabled": app.Database.Execute("UPDATE AspNetUsers SET IsEnabled=0"); break;
            case "revoked": app.Database.Execute("UPDATE auth_sessions SET RevokedAtUtc=CreatedAtUtc"); break;
            case "expired": app.Database.Execute("UPDATE auth_sessions SET ExpiresAtUtc=CreatedAtUtc"); break;
            case "person": app.Database.Execute("DELETE FROM m_user WHERE code=1"); break;
            case "link": app.Database.Execute("DELETE FROM auth_user_links"); break;
            case "store": app.Database.Execute("ALTER TABLE auth_sessions RENAME TO unavailable_sessions"); break;
        }
        var before = app.Database.Snapshot(app.Database.Tables());
        using var result = await client.GetAsync("/admin/accounts");
        Assert.Equal(expected, result.StatusCode);
        Assert.Equal("no-store", result.Headers.CacheControl!.ToString());
        Assert.Null(result.Headers.Location);
        Assert.Empty(await result.Content.ReadAsStringAsync());
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Theory]
    [InlineData("http://monetail.example.test/admin/accounts")]
    [InlineData("https://other.example.test/admin/accounts")]
    [InlineData("https://monetail.example.test:444/admin/accounts")]
    public async Task AdminRoutesRequireTheConfiguredHttpsHostAndPort(string address)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        await app.LoginAsync(client);
        var before = app.Database.Snapshot(app.Database.Tables());
        using var result = await client.GetAsync(address);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("no-store", result.Headers.CacheControl!.ToString());
        Assert.Empty(await result.Content.ReadAsStringAsync());
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task ListQueryFailureReturnsSanitizedServiceUnavailableAndDoesNotChangeAnything()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var owner = app.Client();
        await app.LoginAsync(owner);
        await using var failed = app.NewFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.ConfigureDbContext<AuthDbContext>(options => options.AddInterceptors(new FailedListQuery()))));
        using var client = app.Client(failed);
        client.DefaultRequestHeaders.Add("Cookie", app.SessionCookie.Split(';')[0]);
        var before = app.Database.Snapshot(app.Database.Tables());
        using var result = await client.GetAsync("/admin/accounts");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode);
        var body = await result.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("認証処理を利用できません。時間をおいてやり直してください。", body.GetProperty("message").GetString());
        Assert.False(result.Headers.Contains("Set-Cookie"));
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task PeopleWithoutLinksAndOrphanAccountsAreNotMatchedByDisplayOrLegacyAccountName()
    {
        await using var app = await LoginTestApp.CreateAsync();
        await app.AddAccountAsync(2);
        app.Database.Execute("DELETE FROM auth_user_links WHERE user_code=2");
        using var client = app.Client();
        await app.LoginAsync(client);
        var before = app.Database.Snapshot(app.Database.Tables());
        var rows = (await client.GetFromJsonAsync<JsonElement>("/admin/accounts")).EnumerateArray().ToArray();
        Assert.Equal(4, rows.Length);
        AssertAccount(rows[0], app.UserId, true);
        Assert.All(rows.Skip(1), row => Assert.Equal(JsonValueKind.Null, row.GetProperty("account").ValueKind));
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task HealthOnlyConfigurationDoesNotPublishTheAdminList()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var result = await client.GetAsync("/admin/accounts");
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
    }

    /// <summary>アカウントの公開項目だけが、人物対応と保存済み利用可否に一致することを確認する。</summary>
    private static void AssertAccount(JsonElement person, string id, bool enabled)
    {
        var account = person.GetProperty("account");
        Assert.Equal(new[] { "authUserId", "isEnabled" }, account.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal(id, account.GetProperty("authUserId").GetString());
        Assert.Equal(enabled, account.GetProperty("isEnabled").GetBoolean());
    }

    /// <summary>セッション検証を成功させた後で、人物一覧のSQLだけを失敗させる。</summary>
    private sealed class FailedListQuery : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("LEFT JOIN AspNetUsers", StringComparison.Ordinal))
                throw new SqliteException("synthetic-sensitive-db-detail", 1);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
