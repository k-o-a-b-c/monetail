using Monetail.Windows.Client;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed class ApiEndpointTests
{
    [Theory]
    [InlineData(" https://API.EXAMPLE:443/ ", "https://api.example/")]
    [InlineData("https://api.example:8443", "https://api.example:8443/")]
    [InlineData("https://127.0.0.1:8443/", "https://127.0.0.1:8443/")]
    [InlineData("https://[::1]:8443/", "https://[::1]:8443/")]
    public void NormalizesHttpsOrigin(string input, string expected)
    {
        var endpoint = ApiEndpoint.Parse(input);
        Assert.Equal(expected, endpoint.Value);
        Assert.Equal(ApiEndpoint.Parse(expected), endpoint);
        Assert.Equal(new Uri(expected + "auth/me"), endpoint.Resolve("/auth/me"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("api.example")]
    [InlineData("http://api.example")]
    [InlineData("https://user:password@api.example")]
    [InlineData("https://@api.example/")]
    [InlineData("https://api.example/base/")]
    [InlineData("https://api.example/../")]
    [InlineData("https://api.example/?")]
    [InlineData("https://api.example/#")]
    [InlineData("https://api.example/?key=secret")]
    [InlineData("https://api.example/#fragment")]
    [InlineData("https:\\api.example")]
    [InlineData("https://api.example/\npath")]
    public void RejectsNonOriginUrls(string input) => Assert.Throws<FormatException>(() => ApiEndpoint.Parse(input));

    [Theory]
    [InlineData("https://different.example/auth/me")]
    [InlineData("//different.example/auth/me")]
    [InlineData("http://api.example/auth/me")]
    [InlineData("https://user@api.example/auth/me")]
    [InlineData("/auth/me#fragment")]
    public void CannotResolveOutsideConfiguredOrigin(string path) =>
        Assert.Throws<ArgumentException>(() => ApiEndpoint.Parse("https://api.example").Resolve(path));
}
