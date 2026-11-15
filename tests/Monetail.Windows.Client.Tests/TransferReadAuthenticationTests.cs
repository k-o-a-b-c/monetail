using System.Net;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed partial class TransfersWorkspaceTests
{
    [Theory]
    [InlineData("list")] [InlineData("detail")] [InlineData("suggestions")] [InlineData("autofill")]
    public async Task TransferAuthenticationRefreshReplaysOnlySameGetWithNewBearer(string operation)
    {
        using var f = new Fixture(request => request.RequestUri!.AbsolutePath == "/connect/token" ? RefreshedTokens() : request.Headers.Authorization?.Parameter == "old" ? new(HttpStatusCode.Unauthorized) : Json(Payload(operation)));
        await Read(f, operation); Assert.Equal(3, f.Requests.Count); Assert.Equal(f.Requests[0].Uri, f.Requests[2].Uri); Assert.Equal("old", f.Requests[0].Bearer); Assert.Equal("updated", f.Requests[2].Bearer); Assert.Equal("rotated", f.Store.Current!.RefreshToken);
    }

    [Fact]
    public async Task TransferProactiveRefreshPreservesFocusAndEveryFilterWithoutExtraGet()
    {
        var query = new TransferListQuery("2026-11", Owner, From, " ＡＴＭ&+ ", focusTransferId: Transfer);
        using var f = new Fixture(request => request.RequestUri!.AbsolutePath == "/connect/token" ? RefreshedTokens() : Json(Page())); f.Store.Current = new("old", "refresh", DateTimeOffset.UtcNow.AddSeconds(-1));
        await f.Workspace.ListAsync(query); Assert.Equal(2, f.Requests.Count); Assert.EndsWith("/connect/token", f.Requests[0].Uri);
        Assert.Contains("focusTransferId=" + Transfer.ToString("D"), f.Requests[1].Uri); Assert.Contains("q=" + Uri.EscapeDataString(query.Q!), f.Requests[1].Uri);
    }

    [Fact]
    public async Task TransferAccountChangeIsDetectedAcrossNewLedgerWorkspaceWithoutClearingNewLogin()
    {
        using var f = new Fixture(_ => Json(Page())); await f.Workspace.ListAsync(new("all")); f.Store.Current = new("another", "another-refresh", DateTimeOffset.UtcNow.AddMinutes(10));
        var other = new TransfersWorkspace(f.Api, f.Session, Guid.NewGuid()); await Assert.ThrowsAsync<AuthenticationContextChangedException>(() => other.ListAsync(new("all"))); Assert.Single(f.Requests); Assert.Equal("another", f.Store.Current.AccessToken);
    }

    [Fact]
    public async Task TransferAccountChangeDuringRefreshDoesNotReplayOldRequest()
    {
        Fixture? active = null;
        using var f = new Fixture(request => {
            if (request.RequestUri!.AbsolutePath == "/connect/token") { active!.Store.Current = new("another", "another-refresh", DateTimeOffset.UtcNow.AddMinutes(10)); return RefreshedTokens(); }
            return new(HttpStatusCode.Unauthorized);
        }); active = f;
        await Assert.ThrowsAsync<AuthenticationContextChangedException>(() => f.Workspace.ListAsync(new("all", cursor: "original"))); Assert.Equal(2, f.Requests.Count); Assert.Equal("another", f.Store.Current!.AccessToken);
    }

    [Fact]
    public async Task TransferNewLedgerWorkspaceHasNoCachedRowsOrImplicitFilters()
    {
        var otherId = Guid.NewGuid(); using var f = new Fixture(request => Json(Page([Summary(request.RequestUri!.AbsolutePath.Contains(otherId.ToString("D"), StringComparison.Ordinal) ? otherId : Transfer)])));
        Assert.Equal(Transfer, Assert.Single((await f.Workspace.ListAsync(new("all", q: "first"))).Transfers).TransferId);
        var other = new TransfersWorkspace(f.Api, f.Session, otherId); Assert.Equal(otherId, Assert.Single((await other.ListAsync(new("2026-11"))).Transfers).TransferId);
        Assert.Equal(2, f.Requests.Count); Assert.DoesNotContain("q=", f.Requests[1].Uri); Assert.Contains($"/ledgers/{otherId:D}/transfers", f.Requests[1].Uri);
    }

    [Fact]
    public async Task TransferRejectedRefreshKeepsAuthenticationFailureTypedAndClearsOldTokens()
    {
        using var f = new Fixture(request => request.RequestUri!.AbsolutePath == "/connect/token" ? Json(new { error = "invalid_grant" }, HttpStatusCode.BadRequest) : new(HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<AuthenticationRejectedException>(() => f.Workspace.ListAsync(new("all"))); Assert.Null(f.Store.Current); Assert.Equal(2, f.Requests.Count);
    }

    [Theory]
    [InlineData("list")] [InlineData("detail")] [InlineData("suggestions")] [InlineData("autofill")]
    public async Task TransferConnectionFailureDoesNotRetryOrClearTokens(string operation)
    {
        using var f = new Fixture(_ => throw new HttpRequestException()); await Assert.ThrowsAsync<ApiConnectionException>(() => Read(f, operation)); Assert.Single(f.Requests); Assert.NotNull(f.Store.Current);
    }

    [Theory]
    [InlineData("list")] [InlineData("detail")] [InlineData("suggestions")] [InlineData("autofill")]
    public async Task TransferTimeoutIsDifferentFromCallerCancellation(string operation)
    {
        using var f = new Fixture(_ => throw new TaskCanceledException()); await Assert.ThrowsAsync<ApiConnectionException>(() => Read(f, operation)); Assert.Single(f.Requests); Assert.NotNull(f.Store.Current);
    }

    [Fact]
    public async Task TransferActualSharedTenSecondTimeoutCancelsBlockedBodyRead()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), MonetailApiClient.ConnectionTimeout);
        using var f = new Fixture(_ => new(HttpStatusCode.OK) { Content = new StreamContent(new BlockedStream()) });
        await Assert.ThrowsAsync<ApiConnectionException>(() => f.Workspace.ListAsync(new("all"))); Assert.Single(f.Requests); Assert.NotNull(f.Store.Current);
    }

    [Theory]
    [InlineData("list")] [InlineData("detail")] [InlineData("suggestions")] [InlineData("autofill")]
    public async Task TransferExplicitCancellationDuringBodyReadIsPropagated(string operation)
    {
        using var caller = new CancellationTokenSource(); using var f = new Fixture(_ => new(HttpStatusCode.OK) { Content = new StreamContent(new BlockedStream(caller)) });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Read(f, operation, caller.Token)); Assert.Single(f.Requests); Assert.NotNull(f.Store.Current);
    }

    [Fact]
    public async Task TransferAlreadyCancelledReadSendsNothing()
    {
        using var caller = new CancellationTokenSource(); caller.Cancel(); using var f = new Fixture(_ => throw new InvalidOperationException());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Workspace.ListAsync(new("all"), caller.Token)); Assert.Empty(f.Requests); Assert.NotNull(f.Store.Current);
    }

    private static HttpResponseMessage RefreshedTokens() => Json(new { access_token = "updated", refresh_token = "rotated", token_type = "Bearer", expires_in = 900 });
    private sealed class BlockedStream(CancellationTokenSource? caller = null) : Stream
    {
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) { caller?.Cancel(); await Task.Delay(Timeout.Infinite, token); return 0; }
        public override void Flush() => throw new NotSupportedException(); public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
