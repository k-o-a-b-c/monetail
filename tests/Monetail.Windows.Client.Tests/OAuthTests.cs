using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed class OAuthTests
{
    private static readonly ApiEndpoint Endpoint = ApiEndpoint.Parse("https://api.example.test");

    [Fact]
    public async Task SuccessfulCallbackExchangesMatchingPkceBeforeBrowserSuccess()
    {
        Dictionary<string, string>? exchange = null;
        using var http = new HttpClient(new Handler(async request =>
        {
            exchange = Query(await request.Content!.ReadAsStringAsync());
            return TokenResponse();
        }));
        await using var login = new BrowserOAuthLogin(new MonetailApiClient(Endpoint, http), Endpoint);
        var authorization = Query(login.AuthorizationUri.Query[1..]);
        Assert.Equal("code", authorization["response_type"]);
        Assert.Equal("query", authorization["response_mode"]);
        Assert.Equal("monetail-api offline_access", authorization["scope"]);
        Assert.Equal("S256", authorization["code_challenge_method"]);
        Assert.False(authorization.ContainsKey("client_secret"));
        Assert.Equal("127.0.0.1", login.RedirectUri.Host);
        Assert.Equal("/callback/", login.RedirectUri.AbsolutePath);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var completion = login.CompleteAsync(cancellation.Token);
        var response = await Callback(login.RedirectUri, $"state={authorization["state"]}&code=one-use-code");
        var tokens = await completion;
        Assert.Equal("access", tokens.AccessToken);
        Assert.Equal("authorization_code", exchange!["grant_type"]);
        Assert.Equal("one-use-code", exchange["code"]);
        Assert.Equal(login.RedirectUri.AbsoluteUri, exchange["redirect_uri"]);
        var expected = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(exchange["code_verifier"])))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(expected, authorization["code_challenge"]);
        Assert.Contains("認証が完了しました。", response);
        Assert.Contains("window.close();", response);
        Assert.Contains("Cache-Control: no-store", response);
        Assert.DoesNotContain("one-use-code", response);
    }

    [Theory]
    [InlineData("state=wrong&code=code", "GET", "/callback/")]
    [InlineData("state={state}&state={state}&code=code", "GET", "/callback/")]
    [InlineData("state={state}&code=code&code=second", "GET", "/callback/")]
    [InlineData("state={state}&code=code&error=access_denied", "GET", "/callback/")]
    [InlineData("state={state}&code=", "GET", "/callback/")]
    [InlineData("state={state}&code=code", "POST", "/callback/")]
    [InlineData("state={state}&code=code", "GET", "/wrong/")]
    [InlineData("state={state}&code=%GG", "GET", "/callback/")]
    public async Task StrayOrMalformedRequestDoesNotConsumePendingLogin(string query, string method, string path)
    {
        var exchanges = 0;
        using var http = new HttpClient(new Handler(_ =>
        {
            exchanges++;
            return Task.FromResult(TokenResponse());
        }));
        await using var login = new BrowserOAuthLogin(new MonetailApiClient(Endpoint, http), Endpoint);
        var state = Query(login.AuthorizationUri.Query[1..])["state"];
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var completion = login.CompleteAsync(cancellation.Token);
        var response = await Callback(login.RedirectUri, query.Replace("{state}", state), method, path);
        Assert.Contains("400 Bad Request", response);
        Assert.Equal(0, exchanges);
        Assert.False(completion.IsCompleted);
        await Callback(login.RedirectUri, $"state={state}&code=valid");
        await completion;
        Assert.Equal(1, exchanges);
    }

    [Fact]
    public async Task MatchingOAuthDenialCompletesWithoutTokenExchange()
    {
        using var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("交換を呼ばない")));
        await using var login = new BrowserOAuthLogin(new MonetailApiClient(Endpoint, http), Endpoint);
        var state = Query(login.AuthorizationUri.Query[1..])["state"];
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var completion = login.CompleteAsync(cancellation.Token);
        var response = await Callback(login.RedirectUri, $"state={state}&error=access_denied");
        await Assert.ThrowsAsync<AuthenticationRejectedException>(() => completion);
        Assert.DoesNotContain("認証が完了しました。", response);
    }

    [Fact]
    public async Task ExchangeFailureNeverDisplaysSuccessfulBrowserCompletion()
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
        await using var login = new BrowserOAuthLogin(new MonetailApiClient(Endpoint, http), Endpoint);
        var state = Query(login.AuthorizationUri.Query[1..])["state"];
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var completion = login.CompleteAsync(cancellation.Token);
        var response = await Callback(login.RedirectUri, $"state={state}&code=code");
        await Assert.ThrowsAsync<ApiConnectionException>(() => completion);
        Assert.DoesNotContain("認証が完了しました。", response);
    }

    [Fact]
    public async Task UserCancellationEndsWaitAndDisposedListenerReleasesPort()
    {
        using var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("交換を呼ばない")));
        var login = new BrowserOAuthLogin(new MonetailApiClient(Endpoint, http), Endpoint);
        var port = login.RedirectUri.Port;
        using var cancellation = new CancellationTokenSource();
        var completion = login.CompleteAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => completion);
        await login.DisposeAsync();
        using var client = new TcpClient();
        await Assert.ThrowsAnyAsync<SocketException>(() => client.ConnectAsync(IPAddress.Loopback, port));
    }

    [Fact]
    public async Task EachAttemptHasIndependentStateChallengeAndListener()
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(TokenResponse())));
        var api = new MonetailApiClient(Endpoint, http);
        await using var first = new BrowserOAuthLogin(api, Endpoint);
        await using var second = new BrowserOAuthLogin(api, Endpoint);
        var firstQuery = Query(first.AuthorizationUri.Query[1..]);
        var secondQuery = Query(second.AuthorizationUri.Query[1..]);
        Assert.NotEqual(firstQuery["state"], secondQuery["state"]);
        Assert.NotEqual(firstQuery["code_challenge"], secondQuery["code_challenge"]);
        Assert.NotEqual(first.RedirectUri.Port, second.RedirectUri.Port);
    }

    private static HttpResponseMessage TokenResponse() => new(HttpStatusCode.OK)
    {
        Content = new StringContent("{\"token_type\":\"Bearer\",\"access_token\":\"access\",\"refresh_token\":\"refresh\",\"expires_in\":900}")
    };

    private static async Task<string> Callback(Uri callback, string query, string method = "GET", string path = "/callback/")
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, callback.Port);
        await using var stream = client.GetStream();
        var request = Encoding.ASCII.GetBytes($"{method} {path}?{query} HTTP/1.1\r\nHost: {callback.Authority}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(request);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static Dictionary<string, string> Query(string query) => query.Split('&').Select(part => part.Split('=', 2))
        .ToDictionary(pair => WebUtility.UrlDecode(pair[0]), pair => WebUtility.UrlDecode(pair[1]));

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
