using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Features.Monetail.Ledgers;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>家計簿APIのアカウント別回数と、既存の認証IP枠からの独立を実HTTPで検証する。</summary>
public class LedgerRateLimitTests
{
    [Fact]
    public async Task CreationHasTwentyAttemptsPerAccountWithoutBlockingReadsOrAnotherAccount()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = await LoginAsync(app);
        var before = app.Database.Snapshot(app.Database.Tables());
        for (var index = 0; index < 20; index++)
        {
            using var invalid = await CreateAsync(client, " ");
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using var blocked = await CreateAsync(client, "家計簿");
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        using var readable = await client.GetAsync("/ledgers");
        Assert.Equal(HttpStatusCode.OK, readable.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));

        // 同じ送信元IPでも、別のアカウントの作成枠は消費されない。
        using var other = await LoginAsync(app, account: await app.AddAccountAsync());
        using var allowed = await CreateAsync(other, "別アカウントの家計簿");
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
    }

    [Fact]
    public async Task ReadsShareSixtyAcrossRoutesSessionsAndIpsAndRejectRevokedSessionsAsUnauthorized()
    {
        await using var app = await LoginTestApp.CreateAsync();
        await using var factory = app.NewFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, RemoteAddressFilter>()));
        using var first = await LoginAsync(app, factory, ip: "192.0.2.1");
        using var second = await LoginAsync(app, factory, ip: "192.0.2.2");
        Assert.Equal(2L, app.Database.Scalar("SELECT COUNT(*) FROM auth_sessions"));
        using var created = await CreateAsync(first, "家計簿");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var ledger = (await created.Content.ReadFromJsonAsync<LedgerResponse>())!;
        var before = app.Database.Snapshot(app.Database.Tables());
        for (var index = 0; index < 60; index++)
        {
            var client = index % 2 == 0 ? first : second;
            var path = index % 3 == 0 ? "/ledgers" : $"/ledgers/{ledger.LedgerId}";
            using var read = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        }
        using var blockedList = await first.GetAsync("/ledgers");
        using var blockedDetail = await second.GetAsync($"/ledgers/{ledger.LedgerId}");
        Assert.Equal(HttpStatusCode.TooManyRequests, blockedList.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, blockedDetail.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        using var creation = await CreateAsync(second, "参照枠とは別の作成");
        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);

        using var other = await LoginAsync(app, factory, await app.AddAccountAsync(), "192.0.2.1");
        using var otherRead = await other.GetAsync("/ledgers");
        Assert.Equal(HttpStatusCode.OK, otherRead.StatusCode);
        using var anonymous = app.Client(factory);
        using var anonymousRead = await anonymous.GetAsync("/ledgers");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousRead.StatusCode);

        app.Database.Execute($"UPDATE auth_sessions SET RevokedAtUtc=CreatedAtUtc WHERE AuthUserId='{app.UserId}'");
        using var revokedRead = await first.GetAsync("/ledgers");
        Assert.Equal(HttpStatusCode.Unauthorized, revokedRead.StatusCode);
    }

    [Fact]
    public async Task ExistingAuthenticationLimitStaysIpBasedAndIndependentOfLedgerLimits()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var first = await LoginAsync(app);
        using var second = await LoginAsync(app, account: await app.AddAccountAsync());
        // ログイン2回のoptions／verifyで、共有IP枠を4回消費している。
        for (var index = 4; index < 20; index++)
        {
            using var options = await first.PostAsJsonAsync("/auth/login/options", new { });
            Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        }
        using var blocked = await second.PostAsJsonAsync("/auth/login/options", new { });
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        using var creation = await CreateAsync(first, "家計簿");
        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);
        using var read = await second.GetAsync("/ledgers");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    /// <summary>指定したアカウント・IP・セッションでログインしてCSRFを取得する。</summary>
    private static async Task<HttpClient> LoginAsync(LoginTestApp app, WebApplicationFactory<Program>? factory = null,
        (string UserId, byte[] CredentialId)? account = null, string? ip = null)
    {
        var client = app.Client(factory);
        if (ip is not null) client.DefaultRequestHeaders.Add("X-Test-Remote-IP", ip);
        var options = await app.BeginAsync(client);
        using var response = await app.VerifyAsync(client, account is { } user
            ? app.AssertionFor(options, user.UserId, user.CredentialId) : app.Assertion(options));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await LoginTestApp.CsrfAsync(client);
        return client;
    }

    /// <summary>新しい要求IDで家計簿作成を送信する。</summary>
    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, string name) =>
        client.PostAsJsonAsync("/ledgers", new { name, requestId = Guid.NewGuid() });

    /// <summary>同じテストホスト内で送信元IPの異なる端末を再現する。</summary>
    private sealed class RemoteAddressFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((http, continuation) =>
            {
                http.Connection.RemoteIpAddress = IPAddress.Parse(http.Request.Headers["X-Test-Remote-IP"].FirstOrDefault() ?? "192.0.2.1");
                return continuation(http);
            });
            next(app);
        };
    }
}
