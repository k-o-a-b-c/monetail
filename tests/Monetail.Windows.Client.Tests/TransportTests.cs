using System.Net;
using System.Text;
using System.Text.Json;
using Monetail.Windows.Client;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed class TransportTests
{
    private static readonly ApiEndpoint Endpoint = ApiEndpoint.Parse("https://api.example.test");

    [Fact]
    public async Task ConnectionChecksOAuthMetadataWithOneRequest()
    {
        var paths = new List<string>();
        using var http = CreateHttp(request =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            Assert.Null(request.Headers.Authorization);
            return Metadata();
        });
        var api = new MonetailApiClient(Endpoint, http);

        await api.CheckConnectionAsync(CancellationToken.None);

        Assert.Equal(new[] { "/.well-known/openid-configuration" }, paths);
    }

    [Theory]
    [InlineData("issuer", "https://other.example.test/")]
    [InlineData("issuer", "https://credential@api.example.test/")]
    [InlineData("issuer", "https://api.example.test/#fragment")]
    [InlineData("authorization_endpoint", "https://other.example.test/connect/authorize")]
    [InlineData("authorization_endpoint", "http://api.example.test/connect/authorize")]
    [InlineData("token_endpoint", "https://other.example.test/connect/token")]
    [InlineData("token_endpoint", "https://api.example.test/connect/token?redirect=other")]
    public async Task ConnectionRejectsMetadataThatWouldChangeOAuthDestination(string field, string value)
    {
        using var http = CreateHttp(_ => Metadata(field, value));
        var api = new MonetailApiClient(Endpoint, http);

        await Assert.ThrowsAsync<ApiConnectionException>(() => api.CheckConnectionAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("response_types_supported")]
    [InlineData("code_challenge_methods_supported")]
    public async Task ConnectionRequiresAuthorizationCodeAndS256Capabilities(string field)
    {
        using var http = CreateHttp(_ => Metadata(field, Array.Empty<string>()));
        var api = new MonetailApiClient(Endpoint, http);

        await Assert.ThrowsAsync<ApiConnectionException>(() => api.CheckConnectionAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CodeExchangeSendsPublicClientAndVerifierWithoutClientSecret()
    {
        Dictionary<string, string>? form = null;
        using var http = CreateHttpAsync(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/connect/token", request.RequestUri!.AbsolutePath);
            Assert.Null(request.Headers.Authorization);
            Assert.Equal("application/x-www-form-urlencoded", request.Content!.Headers.ContentType!.MediaType);
            form = ParseForm(await request.Content.ReadAsStringAsync());
            return TokenResponse();
        });
        var api = new MonetailApiClient(Endpoint, http);
        var callback = new Uri("http://127.0.0.1:45678/callback/");

        var tokens = await api.ExchangeCodeAsync("single-use-code", "verifier-value", callback, CancellationToken.None);

        Assert.Equal("authorization_code", form!["grant_type"]);
        Assert.Equal("monetail-windows", form["client_id"]);
        Assert.Equal("single-use-code", form["code"]);
        Assert.Equal("verifier-value", form["code_verifier"]);
        Assert.Equal(callback.AbsoluteUri, form["redirect_uri"]);
        Assert.False(form.ContainsKey("client_secret"));
        Assert.Equal("access-value", tokens.AccessToken);
        Assert.Equal("refresh-value", tokens.RefreshToken);
    }

    [Fact]
    public async Task RefreshUsesOnlyTheRefreshGrantAndPublicClient()
    {
        Dictionary<string, string>? form = null;
        using var http = CreateHttpAsync(async request =>
        {
            Assert.Null(request.Headers.Authorization);
            form = ParseForm(await request.Content!.ReadAsStringAsync());
            return TokenResponse();
        });
        var api = new MonetailApiClient(Endpoint, http);

        await api.RefreshAsync("existing-refresh", CancellationToken.None);

        Assert.Equal("refresh_token", form!["grant_type"]);
        Assert.Equal("monetail-windows", form["client_id"]);
        Assert.Equal("existing-refresh", form["refresh_token"]);
        Assert.False(form.ContainsKey("client_secret"));
        Assert.False(form.ContainsKey("code_verifier"));
    }

    [Fact]
    public async Task AuthenticatedRequestUsesBearerAndReadsServerSessionDeadline()
    {
        var id = Guid.Parse("6923b5ca-8684-4ef7-8545-648e83b1f040");
        var deadline = DateTimeOffset.Parse("2026-11-01T02:00:00Z");
        using var http = CreateHttp(request =>
        {
            Assert.Equal("/auth/me", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("access-value", request.Headers.Authorization.Parameter);
            return Json(new { authUserId = id, displayName = "合成アカウント", expiresAtUtc = deadline,
                verifiedAtUtc = DateTimeOffset.Parse("2026-10-29T10:00:00Z") });
        });
        var api = new MonetailApiClient(Endpoint, http);

        var account = await api.GetCurrentUserAsync("access-value", CancellationToken.None);

        Assert.Equal(id, account.AuthUserId);
        Assert.Equal("合成アカウント", account.DisplayName);
        Assert.Equal(deadline, account.ExpiresAtUtc);
    }

    [Fact]
    public async Task LogoutRevokesOnlyTheBearerSession()
    {
        using var http = CreateHttp(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/auth/logout", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("access-value", request.Headers.Authorization.Parameter);
            return new(HttpStatusCode.NoContent);
        });
        var api = new MonetailApiClient(Endpoint, http);

        await api.LogoutAsync("access-value", CancellationToken.None);
    }

    [Fact]
    public async Task UnauthorizedSessionIsAuthenticationRejection()
    {
        using var http = CreateHttp(_ => new(HttpStatusCode.Unauthorized));
        var api = new MonetailApiClient(Endpoint, http);

        await Assert.ThrowsAsync<AuthenticationRejectedException>(() => api.GetCurrentUserAsync("expired", CancellationToken.None));
    }

    [Fact]
    public async Task InvalidRefreshGrantIsAuthenticationRejection()
    {
        using var http = CreateHttp(_ => Json(new { error = "invalid_grant" }, HttpStatusCode.BadRequest));
        var api = new MonetailApiClient(Endpoint, http);

        await Assert.ThrowsAsync<AuthenticationRejectedException>(() => api.RefreshAsync("revoked", CancellationToken.None));
    }

    [Fact]
    public async Task ServerFailureDoesNotBecomeAuthenticationRejection()
    {
        using var http = CreateHttp(_ => Json(new { error = "invalid_grant" }, HttpStatusCode.ServiceUnavailable));
        var api = new MonetailApiClient(Endpoint, http);

        await Assert.ThrowsAsync<ApiConnectionException>(() => api.RefreshAsync("keep-this-refresh", CancellationToken.None));
    }

    [Fact]
    public async Task RedirectIsConnectionFailureWithoutFollowingAnotherOrigin()
    {
        var calls = 0;
        using var http = CreateHttp(_ =>
        {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("https://other.example.test/auth/me");
            return response;
        });
        var api = new MonetailApiClient(Endpoint, http);

        await Assert.ThrowsAsync<ApiConnectionException>(() => api.GetCurrentUserAsync("do-not-forward", CancellationToken.None));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("{\"access_token\":\"value\",\"token_type\":\"Basic\",\"expires_in\":900}")]
    [InlineData("{\"access_token\":\"\",\"token_type\":\"Bearer\",\"expires_in\":900}")]
    [InlineData("{\"access_token\":\"value\",\"token_type\":\"Bearer\",\"expires_in\":0}")]
    [InlineData("{\"access_token\":\"value\",\"token_type\":\"Bearer\",\"expires_in\":901}")]
    [InlineData("{\"access_token\":\"value\",\"token_type\":\"Bearer\"}")]
    [InlineData("{\"access_token\":\"value\",\"token_type\":\"Bearer\",\"expires_in\":\"900\"}")]
    [InlineData("{\"access_token\":\"value\",\"token_type\":\"Bearer\",\"expires_in\":1.5}")]
    public async Task InvalidTokenResponsesAreProtocolFailures(string payload)
    {
        using var http = CreateHttp(_ => new(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") });
        var api = new MonetailApiClient(Endpoint, http);

        await Assert.ThrowsAsync<ApiConnectionException>(() => api.RefreshAsync("existing-refresh", CancellationToken.None));
    }

    [Fact]
    public async Task UnauthorizedConnectionCheckIsNotEvidenceOfExpiredCredentials()
    {
        using var http = CreateHttp(_ => new(HttpStatusCode.Unauthorized));
        var api = new MonetailApiClient(Endpoint, http);
        await Assert.ThrowsAsync<ApiConnectionException>(() => api.CheckConnectionAsync());
    }

    [Fact]
    public async Task UnauthorizedTokenEndpointIsConfigurationFailureRatherThanInvalidGrant()
    {
        using var http = CreateHttp(_ => new(HttpStatusCode.Unauthorized));
        var api = new MonetailApiClient(Endpoint, http);
        await Assert.ThrowsAsync<ApiConnectionException>(() => api.RefreshAsync("retain-refresh"));
    }

    [Fact]
    public async Task OversizedResponseCannotBeLoadedAsTokenJson()
    {
        using var http = CreateHttp(_ => new(HttpStatusCode.OK)
        {
            Content = new StringContent(new string('x', 65_537))
        });
        var api = new MonetailApiClient(Endpoint, http);
        await Assert.ThrowsAsync<ApiConnectionException>(() => api.RefreshAsync("retain-refresh"));
    }

    private static HttpResponseMessage Metadata(string? field = null, object? value = null)
    {
        var metadata = new Dictionary<string, object>
        {
            ["issuer"] = "https://api.example.test/",
            ["authorization_endpoint"] = "https://api.example.test/connect/authorize",
            ["token_endpoint"] = "https://api.example.test/connect/token",
            ["response_types_supported"] = new[] { "code" },
            ["code_challenge_methods_supported"] = new[] { "S256" }
        };
        if (field is not null) metadata[field] = value!;
        return Json(metadata);
    }

    private static HttpResponseMessage TokenResponse() => Json(new { access_token = "access-value", refresh_token = "refresh-value",
        token_type = "Bearer", expires_in = 900 });

    private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
    };

    private static HttpClient CreateHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) => new(new StubHandler(request => Task.FromResult(respond(request))));
    private static HttpClient CreateHttpAsync(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) => new(new StubHandler(respond));

    private static Dictionary<string, string> ParseForm(string form) => form.Split('&').Select(pair => pair.Split('=', 2))
        .ToDictionary(pair => Uri.UnescapeDataString(pair[0].Replace('+', ' ')), pair => Uri.UnescapeDataString(pair[1].Replace('+', ' ')));

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
