using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Authentication;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>固定HTTPS設定と、プロキシの信頼境界を検証する。</summary>
public class PasskeySettingsTests
{
    [Theory]
    [InlineData("Passkeys:Origin", "http://monetail.example.test")]
    [InlineData("Passkeys:Origin", "https://monetail.example.test/other")]
    [InlineData("Passkeys:Origin", "https://monetail.example.test?code=x")]
    [InlineData("Passkeys:RpId", "other.example.test")]
    [InlineData("Passkeys:DataProtectionKeysPath", "relative/keys")]
    [InlineData("Passkeys:TrustedProxies:0", "*")]
    public void IncompleteOrUnsafeSettingsAreRejected(string key, string value)
    {
        var values = Settings();
        values[key] = value;
        Assert.Throws<InvalidOperationException>(() => PasskeySettings.Read(new ConfigurationBuilder().AddInMemoryCollection(values).Build()));
    }

    [Fact]
    public async Task MissingSettingsDoNotExposeEnrollmentAndConfiguredStartupDoesNotMigrateDatabase()
    {
        await using (var factory = new WebApplicationFactory<Program>())
        using (var client = factory.CreateClient())
        using (var response = await client.GetAsync("/auth/enroll"))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var database = new AuthDatabaseFixture();
        var before = database.Snapshot(database.Tables());
        await using var configured = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            foreach (var pair in Settings()) builder.UseSetting(pair.Key, pair.Value);
            builder.UseSetting("Passkeys:DataProtectionKeysPath", Path.Combine(Path.GetDirectoryName(database.DatabasePath)!, "keys"));
            builder.UseSetting("ConnectionStrings:Monetail", database.ConnectionString);
        });
        using var configuredClient = configured.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://monetail.example.test") });
        using var page = await configuredClient.GetAsync("/auth/enroll");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal(before, database.Snapshot(database.Tables()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ForwardedHostAndHttpsAreAcceptedOnlyFromConfiguredProxy(bool trusted)
    {
        using var database = new AuthDatabaseFixture();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            foreach (var pair in Settings()) builder.UseSetting(pair.Key, pair.Value);
            builder.UseSetting("Passkeys:DataProtectionKeysPath", Path.Combine(Path.GetDirectoryName(database.DatabasePath)!, "keys"));
            builder.UseSetting("ConnectionStrings:Monetail", database.ConnectionString);
            builder.UseSetting("Passkeys:TrustedProxies:0", "192.0.2.1");
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(new RemoteAddressStartupFilter(trusted)));
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", "monetail.example.test");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "192.0.2.100");
        using var response = await client.GetAsync("/auth/enroll");
        Assert.Equal(trusted ? HttpStatusCode.OK : HttpStatusCode.BadRequest, response.StatusCode);
        if (trusted)
        {
            client.DefaultRequestHeaders.Remove("X-Forwarded-Host");
            client.DefaultRequestHeaders.Add("X-Forwarded-Host", "other.example.test");
            using var wrongHost = await client.GetAsync("/auth/enroll");
            Assert.Equal(HttpStatusCode.BadRequest, wrongHost.StatusCode);
        }
    }

    /// <summary>実在の配備先を含まないテスト設定を作る。</summary>
    private static Dictionary<string, string?> Settings() => new()
    {
        ["Passkeys:Origin"] = "https://monetail.example.test",
        ["Passkeys:RpId"] = "monetail.example.test",
        ["Passkeys:DataProtectionKeysPath"] = Path.GetTempPath()
    };

    /// <summary>テスト接続の送信元を固定し、転送ヘッダーの信頼判定を通す。</summary>
    private sealed class RemoteAddressStartupFilter(bool trusted) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((http, continuation) =>
            {
                http.Connection.RemoteIpAddress = IPAddress.Parse(trusted ? "192.0.2.1" : "192.0.2.2");
                return continuation(http);
            });
            next(app);
        };
    }
}
