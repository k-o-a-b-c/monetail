using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed class LedgerWorkspaceTests
{
    private static readonly LedgerInfo First = new(Guid.NewGuid(), "同じ家計簿名", "Viewer", Guid.NewGuid().ToString("N"));
    private static readonly LedgerInfo Second = new(Guid.NewGuid(), "同じ家計簿名", "Editor", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task MissingHistoryRoutesToCreationSingleLedgerOrChoicesAndOnlyOpeningWrites(int count)
    {
        var rows = new[] { First, Second }.Take(count).ToArray();
        using var fixture = new Fixture(request => request.RequestUri!.AbsolutePath switch
        {
            "/ledgers/open-last" => Json(new { ledger = (LedgerInfo?)null }),
            "/ledgers" => Json(rows),
            _ => Json(First)
        });
        var destination = await fixture.Workspace.StartAsync();
        Assert.Equal(count == 1 ? First : null, destination.Opened);
        Assert.Equal(count == 1 ? 0 : count, destination.Choices.Count);
        Assert.Equal(count == 1 ? 2 : 1, fixture.Requests.Count(row => row.Method == HttpMethod.Post));
        Assert.DoesNotContain(fixture.Requests, row => row.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task LastOpenedIsOpenedInOneRequestWithoutAnotherIdLookup()
    {
        using var fixture = new Fixture(request => request.RequestUri!.AbsolutePath == "/ledgers/open-last"
            ? Json(new { ledger = First }) : Json(First));
        var destination = await fixture.Workspace.StartAsync();
        Assert.Equal(First, destination.Opened);
        Assert.Equal(new[] { "/ledgers/open-last" }, fixture.Requests.Select(row => row.Path));
    }

    [Fact]
    public async Task RemovedPreviousSelectionFallsBackToCurrentMembership()
    {
        using var fixture = new Fixture(request => request.RequestUri!.AbsolutePath switch
        {
            "/ledgers/open-last" => Json(new { ledger = (LedgerInfo?)null }),
            "/ledgers" => Json(new[] { Second }),
            var path when path == $"/ledgers/{First.LedgerId:N}/open" => new(HttpStatusCode.NotFound),
            _ => Json(Second)
        });
        Assert.Equal(Second, (await fixture.Workspace.StartAsync()).Opened);
    }

    [Fact]
    public async Task LastOpenedCommunicationFailureDoesNotPretendThereIsNoHistory()
    {
        using var fixture = new Fixture(_ => new(HttpStatusCode.ServiceUnavailable));
        await Assert.ThrowsAsync<ApiConnectionException>(() => fixture.Workspace.StartAsync());
        Assert.Single(fixture.Requests);
        Assert.NotNull(fixture.Store.Current);
    }

    [Fact]
    public async Task CreationRetryUsesTheSameNormalizedNameAndRequestId()
    {
        var bodies = new List<JsonElement>();
        var failed = false;
        using var fixture = new Fixture(request =>
        {
            if (request.RequestUri!.AbsolutePath != "/ledgers") return Json(First);
            bodies.Add(JsonSerializer.Deserialize<JsonElement>(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()));
            if (!failed) { failed = true; return new(HttpStatusCode.ServiceUnavailable); }
            return Json(First);
        });
        var draft = new LedgerCreationDraft("  家計簿  ");
        await Assert.ThrowsAsync<ApiConnectionException>(() => fixture.Workspace.CreateAsync(draft));
        Assert.Equal(First, await fixture.Workspace.CreateAsync(draft));
        Assert.Equal(bodies[0].GetRawText(), bodies[1].GetRawText());
        Assert.Equal("家計簿", bodies[1].GetProperty("name").GetString());
        Assert.Equal(draft.RequestId, bodies[1].GetProperty("requestId").GetGuid());
        Assert.All(fixture.Requests, row => Assert.Equal("/ledgers", row.Path));
        Assert.Equal(2, fixture.Requests.Count);
    }

    [Fact]
    public async Task OpeningReturnsDataAndRecordsTheSelectionInOneRequest()
    {
        using var fixture = new Fixture(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal($"/ledgers/{First.LedgerId:N}/open", request.RequestUri!.AbsolutePath);
            Assert.Null(request.Content);
            return Json(First);
        });
        Assert.Equal(First, await fixture.Workspace.OpenAsync(First.LedgerId));
        Assert.Single(fixture.Requests);
    }

    [Fact]
    public async Task BusinessRequestRefreshesOnceOnUnauthorizedWithoutDiscardingRotatedToken()
    {
        using var fixture = new Fixture(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/connect/token")
                return Json(new { access_token = "new-access", refresh_token = "new-refresh", token_type = "Bearer", expires_in = 900 });
            return request.Headers.Authorization!.Parameter == "old-access" ? new(HttpStatusCode.Unauthorized) : Json(First);
        });
        Assert.Equal(First, await fixture.Workspace.OpenAsync(First.LedgerId));
        Assert.Equal("new-refresh", fixture.Store.Current!.RefreshToken);
        Assert.Equal(3, fixture.Requests.Count);
    }

    [Fact]
    public async Task DefinitiveRejectionAfterRefreshClearsLogin()
    {
        using var fixture = new Fixture(request => request.RequestUri!.AbsolutePath == "/connect/token"
            ? Json(new { access_token = "new-access", refresh_token = "new-refresh", token_type = "Bearer", expires_in = 900 })
            : new(HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<AuthenticationRejectedException>(() => fixture.Workspace.OpenAsync(First.LedgerId));
        Assert.Null(fixture.Store.Current);
        Assert.Equal(3, fixture.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task OperationRejectionDoesNotExpireLogin(HttpStatusCode status)
    {
        using var fixture = new Fixture(_ => new(status));
        var error = await Assert.ThrowsAsync<LedgerRequestException>(() => fixture.Workspace.OpenAsync(First.LedgerId));
        Assert.Equal(status, error.StatusCode);
        Assert.NotNull(fixture.Store.Current);
        Assert.Single(fixture.Requests);
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnotherWindowChangingAuthenticationNeverPerformsAnOperationAsTheOtherAccount(bool logout)
    {
        var endpoint = ApiEndpoint.Parse("https://synthetic-api.example");
        var store = new Store();
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal("/auth/me", request.RequestUri!.AbsolutePath);
            return Json(new { authUserId = Guid.NewGuid(), displayName = "アカウントA",
                expiresAtUtc = DateTimeOffset.UtcNow.AddDays(1), verifiedAtUtc = DateTimeOffset.UtcNow });
        }));
        var api = new MonetailApiClient(endpoint, http);
        var session = new ClientSession(api, endpoint, store);
        Assert.NotNull(await session.RestoreAsync());
        var other = new TokenSet("account-b-access", "account-b-refresh", DateTimeOffset.UtcNow.AddMinutes(10));
        store.Current = other;
        if (logout) await Assert.ThrowsAsync<AuthenticationContextChangedException>(() => session.LogoutAsync());
        else await Assert.ThrowsAsync<AuthenticationContextChangedException>(() => new LedgerWorkspace(api, session).OpenAsync(First.LedgerId));
        Assert.Same(other, store.Current);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient http;
        public Store Store { get; } = new();
        public List<(HttpMethod Method, string Path)> Requests { get; } = [];
        public LedgerWorkspace Workspace { get; }
        public Fixture(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            http = new HttpClient(new Handler(request =>
            {
                Requests.Add((request.Method, request.RequestUri!.AbsolutePath));
                Assert.Equal("https", request.RequestUri.Scheme);
                return respond(request);
            }));
            var endpoint = ApiEndpoint.Parse("https://synthetic-api.example");
            var api = new MonetailApiClient(endpoint, http);
            Workspace = new LedgerWorkspace(api, new ClientSession(api, endpoint, Store));
        }
        public void Dispose() => http.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoginChangedDuringRefreshIsNeitherOverwrittenNorDeleted(bool refreshSucceeded)
    {
        var endpoint = ApiEndpoint.Parse("https://synthetic-api.example");
        var store = new Store();
        var other = new TokenSet("account-b-access", "account-b-refresh", DateTimeOffset.UtcNow.AddMinutes(10));
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/auth/me")
                return Json(new { authUserId = Guid.NewGuid(), displayName = "アカウントA",
                    expiresAtUtc = DateTimeOffset.UtcNow.AddDays(1), verifiedAtUtc = DateTimeOffset.UtcNow });
            if (request.RequestUri.AbsolutePath == "/connect/token")
            {
                store.Current = other;
                return refreshSucceeded
                    ? Json(new { access_token = "rotated-a-access", refresh_token = "rotated-a-refresh", token_type = "Bearer", expires_in = 900 })
                    : new(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { error = "invalid_grant" }) };
            }
            Assert.Equal("old-access", request.Headers.Authorization!.Parameter);
            return new(HttpStatusCode.Unauthorized);
        }));
        var api = new MonetailApiClient(endpoint, http);
        var session = new ClientSession(api, endpoint, store);
        Assert.NotNull(await session.RestoreAsync());
        await Assert.ThrowsAsync<AuthenticationContextChangedException>(() => new LedgerWorkspace(api, session).OpenAsync(First.LedgerId));
        Assert.Same(other, store.Current);
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(respond(request));
    }
    private sealed class Store : ITokenStore
    {
        public TokenSet? Current { get; set; } = new("old-access", "old-refresh", DateTimeOffset.UtcNow.AddMinutes(10));
        public TokenSet? Load(ApiEndpoint endpoint) => Current;
        public void Save(ApiEndpoint endpoint, TokenSet tokens) => Current = tokens;
        public void Clear() => Current = null;
    }
}
