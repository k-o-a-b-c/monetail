using System.Net;
using System.Text;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed partial class TransfersWorkspaceTests
{
    [Theory]
    [InlineData(TransferWriteOperation.Create)] [InlineData(TransferWriteOperation.Update)] [InlineData(TransferWriteOperation.Delete)]
    public async Task TransferWriteManualRetryKeepsRequestAndEditedSaveUsesNewId(TransferWriteOperation operation)
    {
        var snapshot = TransferSnapshot(); var request = TransferRequest(operation, snapshot); var calls = 0;
        using var f = new TransferWriteFixture((_, _) => ++calls == 1 ? throw new HttpRequestException() : Task.FromResult(TransferWritten(operation)));
        var error = await Assert.ThrowsAsync<TransferWriteUncertainException>(() => TransferWrite(f, request));
        Assert.Equal(TransferWriteFailureKind.Connection, error.Kind); Assert.Single(f.Requests); Assert.NotNull(f.Store.Current);
        var edited = snapshot with { Name = "変更後", Amount = 1, Memo = " 編集 " };
        await TransferWrite(f, request); Assert.Equal(f.Requests[0], f.Requests[1]);
        var next = TransferRequest(operation, edited); Assert.NotEqual(request.RequestId, next.RequestId); Assert.Equal(4, next.RequestId.Version);
        await TransferWrite(f, next); Assert.NotEqual(f.Requests[1].Body, f.Requests[2].Body); Assert.Equal(Revision, operation == TransferWriteOperation.Create ? Revision : request.Revision);
    }

    [Theory]
    [InlineData(TransferWriteOperation.Create)] [InlineData(TransferWriteOperation.Update)] [InlineData(TransferWriteOperation.Delete)]
    public async Task TransferWriteRefreshRecreatesContentWithoutChangingRequest(TransferWriteOperation operation)
    {
        using var f = new TransferWriteFixture((http, _) => Task.FromResult(http.RequestUri!.AbsolutePath == "/connect/token" ? RefreshedTokens() : http.Headers.Authorization?.Parameter == "old" ? new(HttpStatusCode.Unauthorized) : TransferWritten(operation)));
        var request = TransferRequest(operation); await TransferWrite(f, request); Assert.Equal(3, f.Requests.Count);
        Assert.Equal(f.Requests[0].Body, f.Requests[2].Body); Assert.Equal(f.Requests[0].Method, f.Requests[2].Method); Assert.Equal(f.Requests[0].Uri, f.Requests[2].Uri);
        Assert.Equal("old", f.Requests[0].Bearer); Assert.Equal("updated", f.Requests[2].Bearer); Assert.Equal("rotated", f.Store.Current!.RefreshToken);
    }

    [Theory]
    [InlineData(TransferWriteOperation.Create)] [InlineData(TransferWriteOperation.Update)] [InlineData(TransferWriteOperation.Delete)]
    public async Task TransferWriteProactiveRefreshDoesNotSendBusinessRequestTwice(TransferWriteOperation operation)
    {
        using var f = new TransferWriteFixture((http, _) => Task.FromResult(http.RequestUri!.AbsolutePath == "/connect/token" ? RefreshedTokens() : TransferWritten(operation)));
        f.Store.Current = new("old", "refresh", DateTimeOffset.UtcNow.AddSeconds(-1));
        await TransferWrite(f, TransferRequest(operation)); Assert.Equal(2, f.Requests.Count); Assert.EndsWith("/connect/token", f.Requests[0].Uri); Assert.Equal("updated", f.Requests[1].Bearer);
    }

    [Fact]
    public async Task TransferWriteRefreshFailureRemainsAuthenticationFailureWithoutBusinessRetry()
    {
        using var f = new TransferWriteFixture((http, _) => Task.FromResult(http.RequestUri!.AbsolutePath == "/connect/token" ? Json(new { error = "invalid_grant" }, HttpStatusCode.BadRequest) : new(HttpStatusCode.Unauthorized)));
        await Assert.ThrowsAsync<AuthenticationRejectedException>(() => f.Workspace.DeleteAsync(TransferRequest(TransferWriteOperation.Delete)));
        Assert.Equal(2, f.Requests.Count); Assert.Null(f.Store.Current);
    }

    [Fact]
    public async Task TransferWriteSecond401StopsAfterOneRefreshAndKeepsSameBody()
    {
        using var f = new TransferWriteFixture((http, _) => Task.FromResult(http.RequestUri!.AbsolutePath == "/connect/token" ? RefreshedTokens() : new(HttpStatusCode.Unauthorized)));
        await Assert.ThrowsAsync<AuthenticationRejectedException>(() => f.Workspace.UpdateAsync(TransferRequest(TransferWriteOperation.Update)));
        Assert.Equal(3, f.Requests.Count); Assert.Equal(f.Requests[0].Body, f.Requests[2].Body); Assert.Null(f.Store.Current);
    }

    [Fact]
    public async Task TransferWriteAccountChangeDuringRefreshDoesNotReplayOrClearNewLogin()
    {
        TransferWriteFixture? active = null;
        using var f = new TransferWriteFixture((http, _) => {
            if (http.RequestUri!.AbsolutePath == "/connect/token") { active!.Store.Current = new("another", "another-refresh", DateTimeOffset.UtcNow.AddMinutes(10)); return Task.FromResult(RefreshedTokens()); }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }); active = f;
        var request = TransferRequest(TransferWriteOperation.Update); await Assert.ThrowsAsync<AuthenticationContextChangedException>(() => f.Workspace.UpdateAsync(request));
        Assert.Equal(2, f.Requests.Count); Assert.Equal("another", f.Store.Current!.AccessToken); Assert.Equal(Revision, request.Revision);
    }

    [Fact]
    public async Task TransferWriteAccountChangeBeforeSendDoesNotCarryOldOperationToNewAccount()
    {
        using var f = new TransferWriteFixture((_, _) => Task.FromResult(TransferWritten(TransferWriteOperation.Create)));
        await f.Workspace.CreateAsync(TransferRequest(TransferWriteOperation.Create)); f.Store.Current = new("another", "another-refresh", DateTimeOffset.UtcNow.AddMinutes(10));
        await Assert.ThrowsAsync<AuthenticationContextChangedException>(() => f.Workspace.CreateAsync(TransferRequest(TransferWriteOperation.Create)));
        Assert.Single(f.Requests); Assert.Equal("another", f.Store.Current.AccessToken);
    }

    [Theory]
    [InlineData(TransferWriteOperation.Create, "http")] [InlineData(TransferWriteOperation.Update, "http")] [InlineData(TransferWriteOperation.Delete, "http")]
    [InlineData(TransferWriteOperation.Create, "io")] [InlineData(TransferWriteOperation.Update, "io")] [InlineData(TransferWriteOperation.Delete, "io")]
    [InlineData(TransferWriteOperation.Create, "timeout")] [InlineData(TransferWriteOperation.Update, "timeout")] [InlineData(TransferWriteOperation.Delete, "timeout")]
    public async Task TransferWriteNetworkFailureAndTimeoutNeverDeclareNotSavedOrRetry(TransferWriteOperation operation, string failure)
    {
        Assert.Equal(TimeSpan.FromSeconds(10), MonetailApiClient.ConnectionTimeout);
        using var f = new TransferWriteFixture((_, _) => throw failure switch { "http" => new HttpRequestException(), "io" => new IOException(), _ => new TaskCanceledException() });
        var error = await Assert.ThrowsAsync<TransferWriteUncertainException>(() => TransferWrite(f, TransferRequest(operation)));
        Assert.Equal(failure == "timeout" ? TransferWriteFailureKind.Timeout : TransferWriteFailureKind.Connection, error.Kind); Assert.Single(f.Requests); Assert.NotNull(f.Store.Current);
    }

    [Theory]
    [InlineData(TransferWriteOperation.Create)] [InlineData(TransferWriteOperation.Update)] [InlineData(TransferWriteOperation.Delete)]
    public async Task TransferWriteInterruptedSuccessBodyIsUncertainAndOnlyExplicitRetryUsesSameBytes(TransferWriteOperation operation)
    {
        var calls = 0;
        using var f = new TransferWriteFixture((_, _) => {
            var response = TransferWritten(operation);
            if (++calls == 1) response.Content = new StreamContent(new ChunkStream(Encoding.UTF8.GetBytes("x"), failAfter: 0));
            return Task.FromResult(response);
        });
        var request = TransferRequest(operation); var error = await Assert.ThrowsAsync<TransferWriteUncertainException>(() => TransferWrite(f, request));
        Assert.Equal(TransferWriteFailureKind.Connection, error.Kind); Assert.Single(f.Requests);
        await TransferWrite(f, request); Assert.Equal(f.Requests[0], f.Requests[1]);
    }

    [Theory]
    [InlineData(TransferWriteOperation.Create)] [InlineData(TransferWriteOperation.Update)] [InlineData(TransferWriteOperation.Delete)]
    public async Task TransferWriteExplicitCancellationDoesNotGuaranteeServerRollbackOrChangeRetry(TransferWriteOperation operation)
    {
        using var caller = new CancellationTokenSource(); var calls = 0;
        using var f = new TransferWriteFixture((_, _) => {
            var response = TransferWritten(operation); if (++calls == 1) response.Content = new StreamContent(new BlockedStream(caller)); return Task.FromResult(response);
        });
        var request = TransferRequest(operation); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TransferWrite(f, request, caller.Token));
        Assert.Single(f.Requests); Assert.NotNull(f.Store.Current); await TransferWrite(f, request); Assert.Equal(f.Requests[0], f.Requests[1]);
    }

    [Fact]
    public async Task TransferWritePrecancelledOperationDoesNotSendAndKeepsRequestReusable()
    {
        using var caller = new CancellationTokenSource(); caller.Cancel(); var request = TransferRequest(TransferWriteOperation.Create);
        using var f = new TransferWriteFixture((_, _) => Task.FromResult(TransferWritten(TransferWriteOperation.Create)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Workspace.CreateAsync(request, caller.Token)); Assert.Empty(f.Requests);
        Assert.Equal(CreatedTransfer, (await f.Workspace.CreateAsync(request)).TransferId); Assert.Single(f.Requests);
    }

    [Fact]
    public async Task TransferWriteMalformedSuccessRetainsExactRequestForExplicitRetry()
    {
        var calls = 0; using var f = new TransferWriteFixture((_, _) => Task.FromResult(++calls == 1 ? new HttpResponseMessage(HttpStatusCode.Created) : TransferWritten(TransferWriteOperation.Create)));
        var request = TransferRequest(TransferWriteOperation.Create); await Assert.ThrowsAsync<TransferWriteUncertainException>(() => f.Workspace.CreateAsync(request)); Assert.Single(f.Requests);
        Assert.Equal(CreatedTransfer, (await f.Workspace.CreateAsync(request)).TransferId); Assert.Equal(f.Requests[0], f.Requests[1]);
    }
}
