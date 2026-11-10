using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed class ReceiptListStateTests
{
    [Fact]
    public void PendingSearchTextIsNotAppliedBySelectionAndInvalidMonthIsAtomic()
    {
        var state = new ReceiptSearchState(new(2026, 12, 31)); state.MonthText = "2027-02"; state.ShopText = "未確定"; state.OwnerPersonId = Guid.NewGuid();
        Assert.Equal("2026-12", state.Query().Month); Assert.Equal("", state.Query().Q); Assert.Equal("未確定", state.ShopText);
        Assert.True(state.ApplyText()); Assert.Equal("2027-02", state.Query().Month); Assert.Equal("未確定", state.Query().Q);
        state.MonthText = "bad"; state.ShopText = "変更中"; Assert.False(state.ApplyText()); Assert.NotNull(state.MonthError);
        Assert.Equal("未確定", state.Query().Q); state.AllPeriods = true; Assert.Equal("all", state.Query().Month);
    }

    [Fact]
    public async Task AutomaticFillStopsAt300AndLayoutCannotRestartItButUserScrollCan()
    {
        var calls = 0;
        using var state = new ReceiptListState((query, _) => Task.FromResult(new ReceiptListInfo(Rows(calls++ * 100, 100), "cursor" + calls)));
        await state.StartAsync(new("all")); var selected = state.Rows[1].ReceiptId; state.SelectedReceiptId = selected;
        await state.ObserveViewportAsync(99, false, false); await state.ObserveViewportAsync(199, false, false);
        Assert.Equal(300, state.Rows.Count); Assert.True(state.CanContinue);
        await state.ObserveViewportAsync(299, false, false); Assert.Equal(3, calls);
        await state.ContinueAsync(); Assert.Equal(400, state.Rows.Count); Assert.True(state.CanContinue);
        await state.ObserveViewportAsync(399, false, false); Assert.Equal(4, calls);
        await state.ObserveViewportAsync(399, true, true); Assert.Equal(500, state.Rows.Count); Assert.Equal(selected, state.SelectedReceiptId);
    }

    [Fact]
    public async Task ResetRejectsLateResponsesFromOldSearchEvenWhenTransportIgnoresCancellation()
    {
        var old = new TaskCompletionSource<ReceiptListInfo>();
        using var state = new ReceiptListState((query, _) => query.Q == "old" ? old.Task : Task.FromResult(new ReceiptListInfo(Rows(500, 1), null)));
        var pending = state.StartAsync(new("all", q: "old")); await state.StartAsync(new("all", q: "new"));
        old.SetResult(new(Rows(0, 100), "old")); await pending; Assert.Single(state.Rows); Assert.Equal(Rows(500, 1)[0].ReceiptId, state.Rows[0].ReceiptId); Assert.Null(state.NextCursor);
    }

    internal static ReceiptSummaryInfo[] Rows(int start, int count) => Enumerable.Range(start + 1, count).Select(i => new ReceiptSummaryInfo(
        Guid.Parse(i.ToString("x32")), new(2026, 11, 10), "店舗" + i, null, Guid.Parse("10000000-0000-0000-0000-000000000000"), i, null)).ToArray();
}
