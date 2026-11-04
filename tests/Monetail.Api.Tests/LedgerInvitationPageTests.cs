using System.Net;
using Xunit;
namespace Monetail.Api.Tests;
public sealed class LedgerInvitationPageTests
{
    [Theory]
    [InlineData("/ledger-invitation")]
    [InlineData("/ledger-invitation.js")]
    public async Task PageIsAnonymousButMaintainsFixedHttpsAndSecurityHeaders(string path)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store",response.Headers.CacheControl!.ToString());
        Assert.Contains("no-referrer",response.Headers.GetValues("Referrer-Policy"));
        Assert.Contains("frame-ancestors 'none'",string.Join("",response.Headers.GetValues("Content-Security-Policy")));
        using var invalid = await client.GetAsync("http://monetail.example.test"+path);
        Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
    }
}
