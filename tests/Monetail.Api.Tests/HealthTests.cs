using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>本番モードでAPIの起動と稼働確認の応答を検証する。</summary>
public class HealthTests
{
    [Fact]
    public async Task HealthReturnsHealthyWithoutDatabaseOrLogin()
    {
        using var database = new AuthDatabaseFixture(createFile: false);
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseApiTestSettings(database));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.False(File.Exists(database.DatabasePath));
    }
}
