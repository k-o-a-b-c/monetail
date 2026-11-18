using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transfers;
using Monetail.Windows.Client.Transport;
using Xunit;
using Monetail.Windows.Presentation.Transfers;
using static Monetail.Windows.Presentation.Tests.TransferListViewModelTests;

namespace Monetail.Windows.Presentation.Tests;

public sealed partial class TransferListViewModelTests
{
    internal static TransferListViewModel Create(Func<TransferListQuery, CancellationToken, Task<TransferListInfo>> load) =>
        new(load, new RecordSearchState(new(2026, 11, 10)), id => id.ToString("D"));
    [Fact]
    public async Task TransferAutomaticFillStopsAt300AndOnlyExplicitContinueOrUserScrollFetchesMore()
    {
        var calls = 0;
        using var state = Create((_, _) => Task.FromResult(new TransferListInfo(Rows(calls++ * 100, 100), "cursor" + calls)));
        await state.StartAsync(new("all")); await state.ObserveViewportAsync(99, false, false); await state.ObserveViewportAsync(199, false, false);
        Assert.Equal(300, state.Rows.Count); Assert.True(state.CanContinue); await state.ObserveViewportAsync(299, false, false); Assert.Equal(3, calls);
        await state.ContinueAsync(); Assert.Equal(400, state.Rows.Count); await state.ObserveViewportAsync(399, false, false); Assert.Equal(4, calls);
        await state.ObserveViewportAsync(399, true, true); Assert.Equal(500, state.Rows.Count); Assert.Equal(5, calls);
    }
    [Fact]
    public async Task SearchChangeRejectsDelayedOldResponseEvenIfTransportIgnoresCancellation()
    {
        var old = new TaskCompletionSource<TransferListInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var state = Create((query, _) => query.Q == "old" ? old.Task : Task.FromResult(new TransferListInfo(Rows(500, 1), null)));
        var pending = state.StartAsync(new("all", q: "old")); await state.StartAsync(new("all", q: "new"));
        var selected = state.Rows[0].TransferId; state.SelectedItem = state.Rows.FirstOrDefault(r => r.TransferId == selected);
        old.SetResult(new(Rows(0, 100), "old")); await pending;
        Assert.Single(state.Rows); Assert.Equal(selected, state.Rows[0].TransferId); Assert.Equal(selected, state.SelectedTransferId);
        Assert.Null(state.NextCursor); Assert.Null(state.Failure); Assert.False(state.IsLoading);
    }
    internal static TransferSummaryInfo[] Rows(int start, int count) => Enumerable.Range(start + 1, count).Select(i => new TransferSummaryInfo(
        Guid.Parse(i.ToString("x32")), new(2026, 11, 10), "振替" + i, Guid.Parse("10000000-0000-0000-0000-000000000000"), Guid.Parse("20000000-0000-0000-0000-000000000000"), i, null)).ToArray();
    [Fact]
    public async Task TransferListInitialAndCursorPagePreserveApiOrderAndSelection()
    {
        var queries = new List<TransferListQuery>();
        using var state = Create((query, _) => { queries.Add(query); return Task.FromResult(new TransferListInfo(Rows(query.Cursor is null ? 0 : 100, query.Cursor is null ? 100 : 1), query.Cursor is null ? "opaque/+" : null)); });
        await state.StartAsync(new("all", q: " 銀行 ")); var selected = state.Rows[3].TransferId; state.SelectedItem = state.Rows.FirstOrDefault(r => r.TransferId == selected);
        Assert.Equal(Rows(0, 100), state.Rows.Select(r => r.Transfer)); await state.ObserveViewportAsync(79, true, true);
        Assert.Equal(101, state.Rows.Count); Assert.Equal(selected, state.SelectedTransferId); Assert.Equal("opaque/+", queries[1].Cursor); Assert.Equal(" 銀行 ", queries[1].Q); Assert.Null(state.NextCursor);
    }
    [Theory]
    [InlineData(99)]
    public async Task TransferListPageCountsDoNotFetchAheadWithoutAnObservation(int count)
    {
        var requests = 0;
        using var state = Create((query, _) => { requests++; var offset = query.Cursor is null ? 0 : int.Parse(query.Cursor); return Task.FromResult(new TransferListInfo(Rows(offset, Math.Min(100, count - offset)), count > offset + 100 ? (offset + 100).ToString() : null)); });
        await state.StartAsync(new("all")); Assert.Equal(1, requests); Assert.Equal(Math.Min(100, count), state.Rows.Count);
        while (state.NextCursor is not null) await state.ObserveViewportAsync(state.Rows.Count - 1, true, true);
        Assert.Equal(count, state.Rows.Count); Assert.Equal(state.Rows.Count, state.Rows.Select(r => r.TransferId).Distinct().Count()); Assert.False(state.IsLoading);
    }
}
