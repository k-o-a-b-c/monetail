using System.Net;
using System.Net.Http.Json;
using System.Text;
using Monetail.Windows.Client;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed class TaxRatesWorkspaceTests
{
    private static object Row(string code = "Standard", string from = "0001-01-01", string? to = null, string? rate = "0") =>
        new { taxCategoryCode = code, validFrom = from, validTo = to, rate };
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    private static HttpResponseMessage Raw(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    [Fact]
    public async Task AllPeriodsAndNullableRatesAreKeptAndSharedAcrossLedgers()
    {
        using var f = new Fixture(_ => Json(new { rates = new[] { Row("ReducedNewspaper", rate: "8"), Row(rate: null), Row("ReducedFood", rate: "8"), Row(from: "2099-01-01", rate: "0"), Row("Exempt", rate: null) } }));
        var first = await f.Workspace.ListAsync(); var second = await new TaxRatesWorkspace(f.Api, f.Session, Guid.NewGuid()).ListAsync();
        Assert.Same(first, second); Assert.Equal(5, first.Rates.Count); Assert.Single(f.Requests);
        Assert.Equal(new[] { "ReducedNewspaper", "Standard", "ReducedFood", "Standard", "Exempt" }, first.Rates.Select(r => r.TaxCategoryCode));
        Assert.Null(first.Rates[1].Rate); Assert.Equal(0m, first.Rates[3].Rate); Assert.Equal(new DateOnly(1, 1, 1), first.Rates[0].ValidFrom);
        Assert.EndsWith("/tax-rates", f.Requests[0].Path); Assert.DoesNotContain('?', f.Requests[0].Path);
    }
    [Theory]
    [InlineData("1989-03-30", false)] [InlineData("1989-03-31", true)] [InlineData("1989-04-01", true)] [InlineData("1989-04-02", false)]
    public void PeriodBothEndsAreInclusive(string day, bool expected) => Assert.Equal(expected,
        new TaxRateInfo("Standard", new(1989, 3, 31), new(1989, 4, 1), null).IsValidOn(DateOnly.Parse(day)));
    [Fact]
    public async Task EmptyHistoryIsCachedAndHasNoFallbackRates()
    {
        using var f = new Fixture(_ => Json(new { rates = Array.Empty<object>() }));
        Assert.Empty((await f.Workspace.ListAsync()).Rates); Assert.Empty((await f.Workspace.ListAsync()).Rates); Assert.Single(f.Requests);
        Assert.True(new TaxRateInfo("Standard", new(2000, 1, 1), null, 0).IsValidOn(DateOnly.MaxValue));
    }
    [Theory]
    [InlineData("{}")] [InlineData("{\"rates\":null}")] [InlineData("{\"rates\":[null]}")]
    [InlineData("{\"rates\":[{\"taxCategoryCode\":\"Other\",\"validFrom\":\"2026-01-01\",\"validTo\":null,\"rate\":\"0\"}]}")]
    [InlineData("{\"rates\":[{\"taxCategoryCode\":\"Exempt\",\"validFrom\":\"2026-01-01\",\"validTo\":null,\"rate\":\"0\"}]}")]
    [InlineData("{\"rates\":[{\"taxCategoryCode\":\"Standard\",\"validFrom\":\"2026-02-30\",\"validTo\":null,\"rate\":null}]}")]
    [InlineData("{\"rates\":[{\"taxCategoryCode\":\"Standard\",\"validFrom\":\"2026-02-01\",\"validTo\":\"2026-01-01\",\"rate\":null}]}")]
    [InlineData("{\"rates\":[{\"taxCategoryCode\":\"Standard\",\"validFrom\":\"2026-01-01\",\"validTo\":null,\"rate\":0}]}")]
    [InlineData("{\"rates\":[{\"taxCategoryCode\":\"Standard\",\"validFrom\":\"2026-01-01\",\"validTo\":null,\"rate\":\"0.1234567890123456789012345678901\"}]}")]
    [InlineData("{\"rates\":[{\"taxCategoryCode\":\"Standard\",\"validFrom\":\"2026-01-01\",\"rate\":null}]}")]
    [InlineData("{\"rates\":[],\"extra\":1}")]
    public async Task InvalidResponseNeverEntersCache(string json)
    {
        using var f = new Fixture(_ => Raw(json)); await Assert.ThrowsAsync<ApiConnectionException>(() => f.Workspace.ListAsync()); Assert.Null(f.Session.TaxRates.Current); Assert.Single(f.Requests);
    }
    [Fact]
    public async Task AllHistoryLargerThan64KiBUsesStreamingAndRemainsExact()
    {
        using var f = new Fixture(_ => Json(new { rates = Enumerable.Range(0, 1500).Select(i => Row(from: new DateOnly(1, 1, 1).AddDays(i).ToString("yyyy-MM-dd"), rate: "0.1234567890123456789012345678")).ToArray() }));
        var result = await f.Workspace.ListAsync(); Assert.Equal(1500, result.Rates.Count); Assert.Equal(0.1234567890123456789012345678m, result.Rates[0].Rate);
    }
    [Theory]
    [InlineData(400)] [InlineData(403)] [InlineData(404)] [InlineData(429)] [InlineData(503)]
    public async Task BusinessErrorIsTypedAndNotRetriedOrCached(int status)
    {
        using var f = new Fixture(_ => new((HttpStatusCode)status) { Content = JsonContent.Create(new { code = "store_unavailable", message = "private SQL", field = (string?)null, latest = (object?)null, permissions = new { currentRole = (string?)null, myPersonId = (Guid?)null } }) });
        var error = await Assert.ThrowsAsync<TaxRateRequestException>(() => f.Workspace.ListAsync()); Assert.Equal((HttpStatusCode)status, error.StatusCode);
        Assert.DoesNotContain("SQL", error.Message); Assert.Single(f.Requests); Assert.Null(f.Session.TaxRates.Current);
    }
    [Fact]
    public async Task RefreshRetriesTheSameReadAndKeepsTheSuccessfulCache()
    {
        using var f = new Fixture(request => request.RequestUri!.AbsolutePath == "/connect/token"
            ? Json(new { access_token = "new", refresh_token = "rotated", token_type = "Bearer", expires_in = 600 })
            : request.Headers.Authorization?.Parameter == "old" ? new(HttpStatusCode.Unauthorized) : Json(new { rates = new[] { Row() } }));
        await f.Workspace.ListAsync(); await f.Workspace.ListAsync(); Assert.Equal(3, f.Requests.Count); Assert.Equal(f.Requests[0].Path, f.Requests[2].Path); Assert.Equal("new", f.Store.Current!.AccessToken);
    }
    [Fact]
    public async Task ChangedAccountRejectsCacheHitAndPreservesTheNewTokens()
    {
        using var f = new Fixture(_ => Json(new { rates = new[] { Row() } })); await f.Workspace.ListAsync();
        f.Store.Current = new("other", "other-refresh", DateTimeOffset.UtcNow.AddMinutes(10));
        await Assert.ThrowsAsync<AuthenticationContextChangedException>(() => f.Workspace.ListAsync()); Assert.Null(f.Session.TaxRates.Current); Assert.Single(f.Requests); Assert.Equal("other", f.Store.Current.AccessToken);
    }
    [Fact]
    public async Task ChangedAccountDuringReadRejectsOldResultAndCache()
    {
        using var f = new Fixture((_, _) => {
            return Task.FromResult(Json(new { rates = new[] { Row() } }));
        });
        // 取得の途中で別ログインのトークンが書き込まれる状況。
        f.DuringRead = () => f.Store.Current = new("other", "r2", DateTimeOffset.UtcNow.AddMinutes(10));
        await Assert.ThrowsAsync<AuthenticationContextChangedException>(() => f.Workspace.ListAsync()); Assert.Null(f.Session.TaxRates.Current);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task LogoutAndAcceptedLoginDiscardCache(bool relogin)
    {
        using var f = new Fixture(request => request.RequestUri!.AbsolutePath == "/auth/me" ? Json(new { authUserId = Guid.NewGuid(), displayName = "テスト", expiresAtUtc = DateTimeOffset.UtcNow.AddDays(1), verifiedAtUtc = DateTimeOffset.UtcNow })
            : request.RequestUri.AbsolutePath == "/auth/logout" ? new(HttpStatusCode.NoContent) : Json(new { rates = new[] { Row() } }));
        await f.Workspace.ListAsync();
        if (relogin) await f.Session.AcceptLoginAsync(new("another", "r2", DateTimeOffset.UtcNow.AddMinutes(10))); else await f.Session.LogoutAsync();
        Assert.Null(f.Session.TaxRates.Current);
        if (relogin) { await f.Workspace.ListAsync(); Assert.Equal(2, f.Requests.Count(r => r.Path.EndsWith("/tax-rates"))); }
    }
    [Fact]
    public async Task ClearedContextRejectsAnOldPendingResultWithoutOverwritingNewCache()
    {
        var cache = new TaxRateHistoryCache(); var pending = new TaskCompletionSource<TaxRatesInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        var old = cache.GetAsync(_ => pending.Task); cache.Clear(); var fresh = new TaxRatesInfo([]);
        Assert.Same(fresh, await cache.GetAsync(_ => Task.FromResult(fresh)));
        pending.SetResult(new([new("Standard", DateOnly.MinValue, null, 10)]));
        await Assert.ThrowsAsync<AuthenticationContextChangedException>(() => old); Assert.Same(fresh, cache.Current);
    }
    [Fact]
    public async Task CancellationAndTransportFailureDoNotCacheOrAutomaticallyRetry()
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        using var f = new Fixture(_ => throw new HttpRequestException("synthetic"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Workspace.ListAsync(cancel.Token)); Assert.Empty(f.Requests);
        await Assert.ThrowsAsync<ApiConnectionException>(() => f.Workspace.ListAsync()); Assert.Single(f.Requests); Assert.Null(f.Session.TaxRates.Current);
    }

    [Fact]
    public async Task ConcurrentLoadsShareOneRequestAndFailedLoadsCanBeRetriedExplicitly()
    {
        var cache = new TaxRateHistoryCache(); var gate = new TaskCompletionSource<TaxRatesInfo>(TaskCreationOptions.RunContinuationsAsynchronously); var reads = 0;
        Task<TaxRatesInfo> Load(CancellationToken _) { reads++; return gate.Task; }
        var first = cache.GetAsync(Load); var second = cache.GetAsync(Load); var value = new TaxRatesInfo([]); gate.SetResult(value);
        Assert.Same(value, await first); Assert.Same(value, await second); Assert.Equal(1, reads);
        cache.Clear(); await Assert.ThrowsAsync<HttpRequestException>(() => cache.GetAsync(_ => throw new HttpRequestException()));
        Assert.Null(cache.Current); Assert.Same(value, await cache.GetAsync(_ => Task.FromResult(value)));
    }
    [Fact]
    public async Task LogoutFailureKeepsTheExistingLoginAndCache()
    {
        using var f = new Fixture(request => request.RequestUri!.AbsolutePath == "/auth/me" ? Json(new { authUserId = Guid.NewGuid(), displayName = "テスト", expiresAtUtc = DateTimeOffset.UtcNow.AddDays(1), verifiedAtUtc = DateTimeOffset.UtcNow })
            : request.RequestUri.AbsolutePath == "/auth/logout" ? new(HttpStatusCode.ServiceUnavailable) : Json(new { rates = new[] { Row() } }));
        var cached = await f.Workspace.ListAsync(); await Assert.ThrowsAsync<ApiConnectionException>(() => f.Session.LogoutAsync());
        Assert.NotNull(f.Store.Current); Assert.Same(cached, f.Session.TaxRates.Current);
    }
    [Fact]
    public async Task RevokedAuthenticationClearsCacheAndDoesNotReuseItAfterRestore()
    {
        using var f = new Fixture(request => request.RequestUri!.AbsolutePath == "/connect/token" ? new(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { error = "invalid_grant" }) }
            : request.RequestUri.AbsolutePath == "/auth/me" ? new(HttpStatusCode.Unauthorized) : Json(new { rates = new[] { Row() } }));
        await f.Workspace.ListAsync(); Assert.Null(await f.Session.RestoreAsync()); Assert.Null(f.Session.TaxRates.Current); Assert.Null(f.Store.Current);
    }
    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient http;
        public readonly PeopleWorkspaceTests.Store Store = new();
        public readonly List<(string Path, string? Bearer)> Requests = [];
        public MonetailApiClient Api { get; }
        public ClientSession Session { get; }
        public TaxRatesWorkspace Workspace { get; }
        public Action? DuringRead;
        public Fixture(Func<HttpRequestMessage, HttpResponseMessage> respond) : this((r, _) => Task.FromResult(respond(r))) { }
        public Fixture(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        {
            var endpoint = ApiEndpoint.Parse("https://tax-test.example");
            http = new(new Handler(async (r, ct) => { Requests.Add((r.RequestUri!.AbsolutePath, r.Headers.Authorization?.Parameter)); var response = await respond(r, ct); DuringRead?.Invoke(); return response; }));
            Api = new(endpoint, http); Session = new(Api, endpoint, Store); Workspace = new(Api, Session, Guid.NewGuid());
        }
        public void Dispose() => http.Dispose();
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => action(request, token);
    }
}
