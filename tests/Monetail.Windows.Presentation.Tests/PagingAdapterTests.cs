using System.Net;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Presentation.Receipts;
using Monetail.Windows.Presentation.Transfers;
using Xunit;

namespace Monetail.Windows.Presentation.Tests;

public sealed class PagingAdapterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleResponsesAreRejectedBeforeProjectingMasterNames(bool transfers)
    {
        var projected = 0;
        var calls = 0;
        if (transfers)
        {
            var old = new TaskCompletionSource<TransferListInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var model = new TransferListViewModel((_, _) => ++calls == 1 ? old.Task : Task.FromResult(new TransferListInfo([], null)),
                new(new(2026, 11, 18)), _ => { projected++; return "現金"; });
            var first = model.StartAsync(new("all"));
            await model.StartAsync(new("2026-11"));
            old.SetResult(new(TransferListViewModelTests.Rows(0, 1), "stale"));
            await first;
            Assert.Empty(model.Rows);
            Assert.Null(model.NextCursor);
        }
        else
        {
            var old = new TaskCompletionSource<ReceiptListInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var model = new ReceiptListViewModel((_, _) => ++calls == 1 ? old.Task : Task.FromResult(new ReceiptListInfo([], null)),
                new(new(2026, 11, 18)), _ => { projected++; return "現金"; });
            var first = model.StartAsync(new("all"));
            await model.StartAsync(new("2026-11"));
            old.SetResult(new(ReceiptListViewModelTests.Rows(0, 1), "stale"));
            await first;
            Assert.Empty(model.Rows);
            Assert.Null(model.NextCursor);
        }
        Assert.Equal(0, projected);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task NormalCursor404InAFocusedTransferListDoesNotRestartTheSearch()
    {
        var calls = new List<TransferListQuery>();
        using var model = TransferListViewModelTests.Create((query, _) =>
        {
            calls.Add(query);
            return query.Cursor is null ? Task.FromResult(new TransferListInfo(TransferListViewModelTests.Rows(0, 100), "next"))
                : throw new TransferRequestException(HttpStatusCode.NotFound, "not_found", null);
        });
        var focus = Guid.NewGuid();
        await model.StartAsync(new("all", focusTransferId: focus));
        await model.ObserveViewportAsync(99, true, true);
        Assert.Equal(2, calls.Count);
        Assert.Null(calls[^1].FocusTransferId);
        Assert.Equal(focus, model.FocusTransferId);
        Assert.IsType<TransferRequestException>(model.Failure);
        Assert.Equal(100, model.Rows.Count);
        Assert.True(model.IsContinuationFailure);
    }
}
