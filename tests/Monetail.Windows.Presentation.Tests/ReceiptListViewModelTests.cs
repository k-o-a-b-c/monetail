using System.Net;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Presentation.Receipts;
using Xunit;

namespace Monetail.Windows.Presentation.Tests;

public sealed class ReceiptListViewModelTests
{
    internal static ReceiptListViewModel Create(Func<ReceiptListQuery, CancellationToken, Task<ReceiptListInfo>> load) =>
        new(load, new(new(2026, 11, 10)), id => id.ToString("D"));
    [Fact]
    public async Task AutomaticFillStopsAt300AndLayoutCannotRestartItButUserScrollCan()
    {
        var calls = 0;
        using var state = ReceiptListViewModelTests.Create((query, _) => Task.FromResult(new ReceiptListInfo(Rows(calls++ * 100, 100), "cursor" + calls)));
        await state.StartAsync(new("all"));
        var selected = state.Rows[1].ReceiptId;
        state.SelectedItem = state.Rows.FirstOrDefault(row => row.ReceiptId == selected);
        await state.ObserveViewportAsync(99, false, false);
        await state.ObserveViewportAsync(199, false, false);
        Assert.Equal(300, state.Rows.Count);
        Assert.True(state.CanContinue);
        await state.ObserveViewportAsync(299, false, false);
        Assert.Equal(3, calls);
        await state.ObserveViewportAsync(299, true, false);
        Assert.False(state.CanContinue);
        Assert.Equal(3, calls);
        await state.ObserveViewportAsync(299, false, false);
        Assert.True(state.CanContinue);
        Assert.Equal(3, calls);
        await state.ContinueAsync();
        Assert.Equal(400, state.Rows.Count);
        Assert.True(state.CanContinue);
        await state.ObserveViewportAsync(399, false, false);
        Assert.Equal(4, calls);
        await state.ObserveViewportAsync(399, true, true);
        Assert.Equal(500, state.Rows.Count);
        Assert.Equal(selected, state.SelectedReceiptId);
    }

    [Fact]
    public async Task ResetRejectsLateResponsesFromOldSearchEvenWhenTransportIgnoresCancellation()
    {
        var old = new TaskCompletionSource<ReceiptListInfo>();
        using var state = ReceiptListViewModelTests.Create((query, _) => query.Q == "old" ? old.Task : Task.FromResult(new ReceiptListInfo(Rows(500, 1), null)));
        var pending = state.StartAsync(new("all", q: "old"));
        await state.StartAsync(new("all", q: "new"));
        old.SetResult(new(Rows(0, 100), "old"));
        await pending;
        Assert.Single(state.Rows);
        Assert.Equal(Rows(500, 1)[0].ReceiptId, state.Rows[0].ReceiptId);
        Assert.Null(state.NextCursor);
    }

    internal static ReceiptSummaryInfo[] Rows(int start, int count) => Enumerable.Range(start + 1, count).Select(i => new ReceiptSummaryInfo(
        Guid.Parse(i.ToString("x32")), new(2026, 11, 10), "店舗" + i, null, Guid.Parse("10000000-0000-0000-0000-000000000000"), i, null)).ToArray();
}
