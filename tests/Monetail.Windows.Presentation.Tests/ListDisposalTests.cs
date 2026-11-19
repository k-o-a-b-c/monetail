using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Presentation.Tests;

public sealed class ListDisposalTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ReceiptDisposalCancelsClearsAndRejectsLateResponsesAfterRecreation(bool append, bool fail)
    {
        var pending = new TaskCompletionSource<ReceiptListInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken readToken = default;
        var reads = 0;
        using var model = ReceiptListViewModelTests.Create((_, token) =>
        {
            if (append && ++reads == 1)
                return Task.FromResult(new ReceiptListInfo(ReceiptListViewModelTests.Rows(0, 100), "next"));
            readToken = token;
            return pending.Task;
        });
        var read = model.StartAsync(new("all"));
        if (append)
        {
            await read;
            model.SelectedItem = model.Rows[0];
            read = model.ObserveViewportAsync(99, true, true);
        }
        var rows = model.Rows;
        model.Dispose();
        model.Dispose();
        Assert.True(readToken.IsCancellationRequested);
        Assert.Same(rows, model.Rows);
        Assert.Empty(rows);
        Assert.Null(model.SelectedItem);
        Assert.Null(model.SelectedReceiptId);
        Assert.False(model.IsLoading);

        using var replacement = ReceiptListViewModelTests.Create((_, _) =>
            Task.FromResult(new ReceiptListInfo(ReceiptListViewModelTests.Rows(500, 1), "current")));
        await replacement.StartAsync(new("all"));
        var selected = Assert.Single(replacement.Rows);
        replacement.SelectedItem = selected;
        if (fail)
            pending.SetException(new HttpRequestException());
        else
            pending.SetResult(new(ReceiptListViewModelTests.Rows(100, 100), "stale"));
        await read;
        Assert.Empty(rows);
        Assert.Null(model.SelectedReceiptId);
        Assert.Null(model.Failure);
        Assert.False(model.IsLoading);
        Assert.Same(selected, Assert.Single(replacement.Rows));
        Assert.Same(selected, replacement.SelectedItem);
        Assert.Equal("current", replacement.NextCursor);
        Assert.Null(replacement.Failure);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TransferDisposalCancelsClearsAndRejectsLateResponsesAfterRecreation(bool append, bool fail)
    {
        var pending = new TaskCompletionSource<TransferListInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken readToken = default;
        var reads = 0;
        using var model = TransferListViewModelTests.Create((_, token) =>
        {
            if (append && ++reads == 1)
                return Task.FromResult(new TransferListInfo(TransferListViewModelTests.Rows(0, 100), "next"));
            readToken = token;
            return pending.Task;
        });
        var read = model.StartAsync(new("all"));
        if (append)
        {
            await read;
            model.SelectedItem = model.Rows[0];
            read = model.ObserveViewportAsync(99, true, true);
        }
        var rows = model.Rows;
        model.Dispose();
        model.Dispose();
        Assert.True(readToken.IsCancellationRequested);
        Assert.Same(rows, model.Rows);
        Assert.Empty(rows);
        Assert.Null(model.SelectedItem);
        Assert.Null(model.SelectedTransferId);
        Assert.False(model.IsLoading);

        using var replacement = TransferListViewModelTests.Create((_, _) =>
            Task.FromResult(new TransferListInfo(TransferListViewModelTests.Rows(500, 1), "current")));
        await replacement.StartAsync(new("all"));
        var selected = Assert.Single(replacement.Rows);
        replacement.SelectedItem = selected;
        if (fail)
            pending.SetException(new HttpRequestException());
        else
            pending.SetResult(new(TransferListViewModelTests.Rows(100, 100), "stale"));
        await read;
        Assert.Empty(rows);
        Assert.Null(model.SelectedTransferId);
        Assert.Null(model.Failure);
        Assert.False(model.IsLoading);
        Assert.Same(selected, Assert.Single(replacement.Rows));
        Assert.Same(selected, replacement.SelectedItem);
        Assert.Equal("current", replacement.NextCursor);
        Assert.Null(replacement.Failure);
    }
}
