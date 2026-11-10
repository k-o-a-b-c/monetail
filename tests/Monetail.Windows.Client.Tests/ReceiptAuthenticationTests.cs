using System.Net;
using System.Text.Json.Nodes;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed partial class ReceiptsWorkspaceTests
{
    [Theory]
    [InlineData(400, "invalid_cursor")] [InlineData(400, "invalid_input")] [InlineData(403, "forbidden")]
    [InlineData(404, "not_found")] [InlineData(429, "rate_limited")] [InlineData(503, "store_unavailable")]
    public async Task BusinessErrorsRemainTypedAndDoNotRetryClearTokensOrExposePrivatePayload(int status, string code)
    {
        using var f = new Fixture(_ => Json(new { code, field = "cursor", message = "非公開のSQLや接続情報", latest = "参照禁止", permissions = new { currentRole = "Viewer", myPersonId = Owner } }, (HttpStatusCode)status));
        var error = await Assert.ThrowsAsync<ReceiptRequestException>(() => f.Workspace.ListAsync(new("all", cursor: "unchanged")));
        Assert.Equal((HttpStatusCode)status, error.StatusCode); Assert.Equal(code, error.Code); Assert.Equal("cursor", error.Field);
        Assert.DoesNotContain("SQL", error.Message); Assert.Single(f.Requests); Assert.NotNull(f.Store.Current);
        if (status is 403 or 404) { Assert.Equal("Viewer", error.Permissions!.CurrentRole); Assert.Equal(Owner, error.Permissions.MyPersonId); }
    }

    [Theory]
    [InlineData(400)] [InlineData(403)] [InlineData(404)] [InlineData(429)] [InlineData(503)]
    public async Task EmptyErrorBodyRetainsKnownStatus(int status)
    {
        using var f = new Fixture(_ => new((HttpStatusCode)status));
        var error = await Assert.ThrowsAsync<ReceiptRequestException>(() => f.Workspace.ListAsync(new("all"))); Assert.Equal((HttpStatusCode)status, error.StatusCode); Assert.NotNull(f.Store.Current);
    }

    [Theory]
    [InlineData("list")] [InlineData("detail")] [InlineData("suggestions")] [InlineData("autofill")]
    public async Task AuthenticationRefreshKeepsTheSameReadQueryAndOnlyReplacesBearer(string operation)
    {
        using var f = new Fixture(request => request.RequestUri!.AbsolutePath == "/connect/token" ? RefreshedTokens() : request.Headers.Authorization?.Parameter == "old"
            ? new(HttpStatusCode.Unauthorized) : ReadPayload(operation));
        await Read(f, operation); Assert.Equal(3, f.Requests.Count); Assert.Equal(f.Requests[0].Uri, f.Requests[2].Uri);
        Assert.Equal("old", f.Requests[0].Bearer); Assert.Equal("updated", f.Requests[2].Bearer); Assert.Equal("rotated", f.Store.Current!.RefreshToken);
    }

    [Fact]
    public async Task RefreshPreservesEveryOpaqueListConditionAndProactiveRefreshDoesNotIssueAnExtraGet()
    {
        var query = new ReceiptListQuery("2026-11", Owner, Payment, " ＡＢＣ+& ", "old/opaque+cursor");
        using var f = new Fixture(request => request.RequestUri!.AbsolutePath == "/connect/token" ? RefreshedTokens() : Json(Page()));
        f.Store.Current = new("old", "refresh", DateTimeOffset.UtcNow.AddSeconds(-1)); await f.Workspace.ListAsync(query);
        Assert.Equal(2, f.Requests.Count); Assert.EndsWith("/connect/token", f.Requests[0].Uri);
        Assert.Contains("cursor=" + Uri.EscapeDataString(query.Cursor!), f.Requests[1].Uri); Assert.Contains("q=" + Uri.EscapeDataString(query.Q!), f.Requests[1].Uri);
    }

    [Fact]
    public async Task AccountChangeBeforeReadIsRejectedWithoutSendingOrClearingNewLogin()
    {
        using var f = new Fixture(_ => Json(Page())); await f.Workspace.ListAsync(new("all"));
        f.Store.Current = new("another", "another-refresh", DateTimeOffset.UtcNow.AddMinutes(10));
        await Assert.ThrowsAsync<AuthenticationContextChangedException>(() => f.Workspace.ListAsync(new("all")));
        Assert.Single(f.Requests); Assert.Equal("another", f.Store.Current.AccessToken);
    }

    [Fact]
    public async Task AccountChangeDuringRefreshDoesNotReplayOldRead()
    {
        Fixture? fixture = null;
        using var f = new Fixture(request => {
            if (request.RequestUri!.AbsolutePath == "/connect/token") { fixture!.Store.Current = new("another", "another-refresh", DateTimeOffset.UtcNow.AddMinutes(10)); return RefreshedTokens(); }
            return new(HttpStatusCode.Unauthorized);
        }); fixture = f;
        await Assert.ThrowsAsync<AuthenticationContextChangedException>(() => f.Workspace.ListAsync(new("all", cursor: "original")));
        Assert.Equal(2, f.Requests.Count); Assert.Equal("another", f.Store.Current!.AccessToken);
    }

    [Fact]
    public async Task RejectedRefreshKeepsAuthenticationErrorTypedAndClearsOnlyOldContext()
    {
        using var f = new Fixture(request => request.RequestUri!.AbsolutePath == "/connect/token" ? Json(new { error = "invalid_grant" }, HttpStatusCode.BadRequest) : new(HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<AuthenticationRejectedException>(() => f.Workspace.ListAsync(new("all"))); Assert.Null(f.Store.Current); Assert.Equal(2, f.Requests.Count);
    }

    [Theory]
    [InlineData("list")] [InlineData("detail")] [InlineData("suggestions")] [InlineData("autofill")]
    public async Task ConnectionFailureDoesNotAutoRetryOrLoseAuthentication(string operation)
    {
        using var f = new Fixture(_ => throw new HttpRequestException());
        await Assert.ThrowsAsync<ApiConnectionException>(() => Read(f, operation)); Assert.Single(f.Requests); Assert.NotNull(f.Store.Current);
    }

    [Fact]
    public async Task CallerCancellationDuringBodyReadIsPropagatedAndPreservesTokens()
    {
        using var cancellation = new CancellationTokenSource();
        using var f = new Fixture(_ => new(HttpStatusCode.OK) { Content = new StreamContent(new CancelledStream(cancellation)) });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Workspace.ListAsync(new("all"), cancellation.Token)); Assert.Single(f.Requests); Assert.NotNull(f.Store.Current);
    }

    [Fact]
    public async Task PreCancelledReadDoesNotSendHttp()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); using var f = new Fixture(_ => throw new InvalidOperationException());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Workspace.ListAsync(new("all"), cancellation.Token)); Assert.Empty(f.Requests); Assert.NotNull(f.Store.Current);
    }

    private static HttpResponseMessage ReadPayload(string operation) => Json(operation switch {
        "list" => Page(), "detail" => Detail(), "suggestions" => new { suggestions = Array.Empty<string>(), hasMore = false },
        _ => new { paymentMethodId = (Guid?)null, taxInclusionMode = (int?)0, taxRoundingMode = "floor" }
    });
    private static HttpResponseMessage RefreshedTokens() => Json(new { access_token = "updated", refresh_token = "rotated", token_type = "Bearer", expires_in = 900 });
    private sealed class CancelledStream(CancellationTokenSource caller) : Stream
    {
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) { caller.Cancel(); await Task.Delay(Timeout.Infinite, token); return 0; }
        public override void Flush() => throw new NotSupportedException(); public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
