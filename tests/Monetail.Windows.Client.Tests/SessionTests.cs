using System.Net;
using System.Text;
using System.Text.Json;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed class SessionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-29T10:00:00Z");
    private static readonly ApiEndpoint Endpoint = ApiEndpoint.Parse("https://api.example.test/");
    private static readonly Guid AccountId = Guid.Parse("a2c0bd76-85c0-4906-93e2-b4e42c990412");

    [Fact]
    public async Task ValidSavedLoginRequiresServerConfirmationAndKeepsItsAbsoluteDeadline()
    {
        var saved = Saved();
        var store = new MemoryTokenStore(saved);
        using var http = Http(request =>
        {
            AssertBearer(request, "/auth/me", "old-access");
            return Account();
        });
        var session = Session(http, store);

        var account = await session.RestoreAsync();

        Assert.Equal(AccountId, account!.AuthUserId);
        Assert.Equal(Now.AddDays(2), account.ExpiresAtUtc);
        Assert.Same(saved, store.Current);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(0, store.ClearCount);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(30)]
    public async Task ExpiredOrNearlyExpiredAccessRefreshesAndSavesRotationBeforeMe(int remainingSeconds)
    {
        var store = new MemoryTokenStore(Saved(remainingSeconds));
        var calls = 0;
        using var http = Http(request =>
        {
            if (++calls == 1)
            {
                Assert.Equal("/connect/token", request.RequestUri!.AbsolutePath);
                return Rotated();
            }
            AssertBearer(request, "/auth/me", "new-access");
            Assert.Equal("new-refresh", store.Current!.RefreshToken);
            return Account();
        });

        var account = await Session(http, store).RestoreAsync();

        Assert.NotNull(account);
        Assert.Equal(2, calls);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal(Now.AddMinutes(15), store.Current!.AccessTokenExpiresAtUtc);
    }

    [Fact]
    public async Task UnauthorizedAccessRetriesWithRefreshOnce()
    {
        var store = new MemoryTokenStore(Saved());
        var paths = new List<string>();
        using var http = Http(request =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            if (paths.Count == 1) return new(HttpStatusCode.Unauthorized);
            if (paths.Count == 2) return Rotated();
            AssertBearer(request, "/auth/me", "new-access");
            return Account();
        });

        Assert.NotNull(await Session(http, store).RestoreAsync());

        Assert.Equal(new[] { "/auth/me", "/connect/token", "/auth/me" }, paths);
        Assert.Equal("new-refresh", store.Current!.RefreshToken);
        Assert.Equal(0, store.ClearCount);
    }

    [Fact]
    public async Task RefreshResponseWithoutReplacementKeepsExistingRefreshToken()
    {
        var store = new MemoryTokenStore(Saved(-1));
        using var http = Http(request => request.RequestUri!.AbsolutePath == "/connect/token"
            ? Json(new { access_token = "new-access", token_type = "Bearer", expires_in = 900 }) : Account());

        Assert.NotNull(await Session(http, store).RestoreAsync());

        Assert.Equal("new-access", store.Current!.AccessToken);
        Assert.Equal("old-refresh", store.Current.RefreshToken);
    }

    [Fact]
    public async Task RejectionAfterRefreshClearsLoginWithoutASecondRefresh()
    {
        var store = new MemoryTokenStore(Saved(-1));
        var calls = 0;
        using var http = Http(_ => ++calls == 1 ? Rotated() : new(HttpStatusCode.Unauthorized));

        Assert.Null(await Session(http, store).RestoreAsync());

        Assert.Equal(2, calls);
        Assert.Null(store.Current);
        Assert.Equal(1, store.ClearCount);
    }

    [Fact]
    public async Task InvalidRefreshGrantClearsLogin()
    {
        var store = new MemoryTokenStore(Saved(-1));
        using var http = Http(_ => Json(new { error = "invalid_grant" }, HttpStatusCode.BadRequest));

        Assert.Null(await Session(http, store).RestoreAsync());

        Assert.Null(store.Current);
        Assert.Equal(1, store.ClearCount);
    }

    [Fact]
    public async Task ExpiredAccessWithoutRefreshReturnsToLoginWithoutSendingAnExpiredToken()
    {
        var store = new MemoryTokenStore(new TokenSet("expired", null, Now.AddSeconds(-1)));
        using var http = Http(_ => throw new Xunit.Sdk.XunitException("通信は不要です。"));

        Assert.Null(await Session(http, store).RestoreAsync());

        Assert.Equal(1, store.ClearCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ServerFailureDuringMeOrRefreshPreservesSavedAuthentication(bool expired)
    {
        var saved = Saved(expired ? -1 : 300);
        var store = new MemoryTokenStore(saved);
        using var http = Http(_ => new(HttpStatusCode.ServiceUnavailable));

        await Assert.ThrowsAsync<ApiConnectionException>(() => Session(http, store).RestoreAsync());

        Assert.Same(saved, store.Current);
        Assert.Equal(0, store.ClearCount);
    }

    [Fact]
    public async Task FailureAfterSuccessfulRefreshPreservesTheRotatedToken()
    {
        var store = new MemoryTokenStore(Saved(-1));
        using var http = Http(request => request.RequestUri!.AbsolutePath == "/connect/token"
            ? Rotated() : new(HttpStatusCode.ServiceUnavailable));

        await Assert.ThrowsAsync<ApiConnectionException>(() => Session(http, store).RestoreAsync());

        Assert.Equal("new-refresh", store.Current!.RefreshToken);
        Assert.Equal(0, store.ClearCount);
    }

    [Fact]
    public async Task TimeoutPreservesSavedAuthentication()
    {
        var saved = Saved();
        var store = new MemoryTokenStore(saved);
        using var http = Http(_ => throw new TaskCanceledException());

        await Assert.ThrowsAsync<ApiConnectionException>(() => Session(http, store).RestoreAsync());

        Assert.Same(saved, store.Current);
        Assert.Equal(0, store.ClearCount);
    }

    [Fact]
    public async Task MissingOrUnrecoverableDpapiLoginDoesNotSendAuthentication()
    {
        var store = new MemoryTokenStore(null);
        using var http = Http(_ => throw new Xunit.Sdk.XunitException("復元できない情報は送信しません。"));

        Assert.Null(await Session(http, store).RestoreAsync());

        Assert.Equal(0, store.SaveCount);
        Assert.Equal(0, store.ClearCount);
    }

    [Fact]
    public async Task NewLoginIsSavedBeforeItsAccountCheckSoNetworkFailureCanBeRetried()
    {
        var acquired = new TokenSet("acquired-access", "acquired-refresh", Now.AddMinutes(15));
        var store = new MemoryTokenStore(null);
        using var http = Http(request =>
        {
            Assert.Same(acquired, store.Current);
            AssertBearer(request, "/auth/me", "acquired-access");
            return new(HttpStatusCode.ServiceUnavailable);
        });

        await Assert.ThrowsAsync<ApiConnectionException>(() => Session(http, store).AcceptLoginAsync(acquired));

        Assert.Same(acquired, store.Current);
        Assert.Equal(0, store.ClearCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task LogoutClearsOnlyAfterServerSuccessOrDefinitiveRejection(HttpStatusCode logoutStatus)
    {
        var store = new MemoryTokenStore(Saved());
        using var http = Http(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/auth/me") return Account();
            Assert.Equal(HttpMethod.Post, request.Method);
            AssertBearer(request, "/auth/logout", "old-access");
            return new(logoutStatus);
        });

        await Session(http, store).LogoutAsync();

        Assert.Null(store.Current);
        Assert.Equal(1, store.ClearCount);
    }

    [Fact]
    public async Task LogoutNetworkFailureKeepsLoginForRetry()
    {
        var saved = Saved();
        var store = new MemoryTokenStore(saved);
        using var http = Http(request => request.RequestUri!.AbsolutePath == "/auth/me"
            ? Account() : new(HttpStatusCode.ServiceUnavailable));

        await Assert.ThrowsAsync<ApiConnectionException>(() => Session(http, store).LogoutAsync());

        Assert.Same(saved, store.Current);
        Assert.Equal(0, store.ClearCount);
    }

    [Fact]
    public async Task TokenFileIoFailureIsNotTreatedAsRevocation()
    {
        var saved = Saved();
        var store = new MemoryTokenStore(saved) { LoadFailure = new IOException("合成読込失敗") };
        using var http = Http(_ => throw new Xunit.Sdk.XunitException("読込失敗後は通信しません。"));

        await Assert.ThrowsAsync<IOException>(() => Session(http, store).RestoreAsync());

        Assert.Same(saved, store.Current);
        Assert.Equal(0, store.ClearCount);
    }

    [Fact]
    public async Task RefreshSaveFailurePreservesExistingFileAndStopsFurtherUse()
    {
        var saved = Saved(-1);
        var store = new MemoryTokenStore(saved) { SaveFailure = new IOException("合成保存失敗") };
        var requests = 0;
        using var http = Http(request =>
        {
            requests++;
            Assert.Equal("/connect/token", request.RequestUri!.AbsolutePath);
            return Rotated();
        });

        await Assert.ThrowsAsync<IOException>(() => Session(http, store).RestoreAsync());

        Assert.Equal(1, requests);
        Assert.Same(saved, store.Current);
        Assert.Equal(0, store.ClearCount);
    }

    [Fact]
    public async Task CallerCancellationPreservesSavedAuthentication()
    {
        var saved = Saved();
        var store = new MemoryTokenStore(saved);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var http = Http(_ => throw new Xunit.Sdk.XunitException("キャンセル後は通信しません。"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Session(http, store).RestoreAsync(cancellation.Token));

        Assert.Same(saved, store.Current);
        Assert.Equal(0, store.ClearCount);
    }

    private static ClientSession Session(HttpClient http, MemoryTokenStore store) =>
        new(new MonetailApiClient(Endpoint, http, new FixedTime()), Endpoint, store, new FixedTime());

    private static TokenSet Saved(int remainingSeconds = 300) => new("old-access", "old-refresh", Now.AddSeconds(remainingSeconds));

    private static HttpResponseMessage Account() => Json(new
    {
        authUserId = AccountId, displayName = "合成アカウント", expiresAtUtc = Now.AddDays(2), verifiedAtUtc = Now.AddHours(-1)
    });

    private static HttpResponseMessage Rotated() => Json(new
    {
        access_token = "new-access", refresh_token = "new-refresh", token_type = "Bearer", expires_in = 900
    });

    private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
    };

    private static void AssertBearer(HttpRequestMessage request, string path, string expected)
    {
        Assert.Equal(path, request.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal(expected, request.Headers.Authorization.Parameter);
    }

    private static HttpClient Http(Func<HttpRequestMessage, HttpResponseMessage> respond) => new(new StubHandler(respond));

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class MemoryTokenStore(TokenSet? saved) : ITokenStore
    {
        public TokenSet? Current { get; private set; } = saved;
        public int SaveCount { get; private set; }
        public int ClearCount { get; private set; }
        public IOException? LoadFailure { get; init; }
        public IOException? SaveFailure { get; init; }
        public TokenSet? Load(ApiEndpoint endpoint)
        {
            Assert.Equal(Endpoint, endpoint);
            if (LoadFailure is not null) throw LoadFailure;
            return Current;
        }
        public void Save(ApiEndpoint endpoint, TokenSet tokens)
        {
            Assert.Equal(Endpoint, endpoint);
            if (SaveFailure is not null) throw SaveFailure;
            Current = tokens;
            SaveCount++;
        }
        public void Clear() { Current = null; ClearCount++; }
    }
}
