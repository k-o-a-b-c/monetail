using System.Net;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed class ReceiptPagingTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(100)] [InlineData(101)] [InlineData(201)]
    public async Task PagesPreserveApiOrderAndAppliedConditions(int count)
    {
        var queries = new List<ReceiptListQuery>(); var owner = Guid.NewGuid(); var payment = Guid.NewGuid();
        using var state = new ReceiptListState((q, _) =>
        {
            queries.Add(q); var offset = q.Cursor is null ? 0 : int.Parse(q.Cursor);
            return Task.FromResult(new ReceiptListInfo(ReceiptListStateTests.Rows(offset, Math.Min(100, count - offset)), offset + 100 < count ? (offset + 100).ToString() : null));
        });
        await state.StartAsync(new("2026-11", owner, payment, "店舗"));
        while (state.NextCursor is not null) await state.ObserveViewportAsync(state.Rows.Count - 1, true, true);
        Assert.Equal(count, state.Rows.Count); Assert.Equal(ReceiptListStateTests.Rows(0, count), state.Rows);
        Assert.All(queries, q => { Assert.Equal("2026-11", q.Month); Assert.Equal(owner, q.OwnerPersonId); Assert.Equal(payment, q.PaymentMethodId); Assert.Equal("店舗", q.Q); });
        Assert.Equal(Enumerable.Range(1, Math.Max(1, (count + 99) / 100)).Select(i => i == 1 ? null : ((i - 1) * 100).ToString()), queries.Select(q => q.Cursor));
    }

    [Fact]
    public async Task PrefetchBoundaryAndConcurrentNotificationsIssueOnlyOneRequest()
    {
        var pending = new TaskCompletionSource<ReceiptListInfo>(); var calls = 0;
        using var state = new ReceiptListState((_, _) => ++calls == 1 ? Task.FromResult(new ReceiptListInfo(ReceiptListStateTests.Rows(0, 100), "next")) : pending.Task);
        await state.StartAsync(new("all"));
        await state.ObserveViewportAsync(78, true, true); Assert.Equal(1, calls);
        var read = state.ObserveViewportAsync(79, true, true); Assert.True(state.IsContinuationLoading);
        await state.ObserveViewportAsync(99, true, true); await state.ContinueAsync(); await state.RetryAsync(); Assert.Equal(2, calls);
        pending.SetResult(new(ReceiptListStateTests.Rows(100, 1), null)); await read;
        Assert.Equal(101, state.Rows.Count); Assert.False(state.CanContinue);
    }

    [Fact]
    public async Task ErrorsRequireManualRetryAndKeepAppendedRowsSelectionAndCursor()
    {
        var calls = 0; var cursors = new List<string?>(); var offline = new HttpRequestException();
        using var state = new ReceiptListState((q, _) =>
        {
            cursors.Add(q.Cursor); calls++;
            return calls switch { 1 or 3 => Task.FromException<ReceiptListInfo>(offline), 2 => Task.FromResult(new ReceiptListInfo(ReceiptListStateTests.Rows(0, 100), "next")), _ => Task.FromResult(new ReceiptListInfo(ReceiptListStateTests.Rows(100, 1), null)) };
        });
        await state.StartAsync(new("all")); Assert.True(state.IsInitialFailure); Assert.Empty(state.Rows);
        await state.ObserveViewportAsync(0, false, false); Assert.Equal(1, calls);
        await state.RetryAsync(); var selected = state.Rows[0].ReceiptId; state.SelectedReceiptId = selected;
        await state.ObserveViewportAsync(99, true, true); Assert.True(state.IsContinuationFailure); Assert.Equal(100, state.Rows.Count); Assert.Equal(selected, state.SelectedReceiptId);
        await state.ObserveViewportAsync(99, true, true); Assert.Equal(3, calls);
        await state.RetryAsync(); Assert.Equal(101, state.Rows.Count); Assert.Equal(selected, state.SelectedReceiptId);
        Assert.Equal(new string?[] { null, null, "next", "next" }, cursors);
    }

    [Fact]
    public async Task InvalidCursorRestartsOnceThenStopsAndManualRetryNeverResendsRejectedCursor()
    {
        var cursors = new List<string?>(); var calls = 0;
        using var state = new ReceiptListState((q, _) =>
        {
            cursors.Add(q.Cursor); calls++;
            return q.Cursor is not null ? Task.FromException<ReceiptListInfo>(new ReceiptRequestException(HttpStatusCode.BadRequest, "invalid_cursor", null))
                : Task.FromResult(new ReceiptListInfo(ReceiptListStateTests.Rows(calls * 100, 100), "next"));
        });
        await state.StartAsync(new("all")); state.SelectedReceiptId = state.Rows[0].ReceiptId;
        await state.ObserveViewportAsync(99, true, true); Assert.Equal(3, calls); Assert.Equal(100, state.Rows.Count); Assert.Null(state.SelectedReceiptId);
        await state.ObserveViewportAsync(99, true, true); Assert.Equal(4, calls); Assert.True(state.IsInitialFailure); Assert.Empty(state.Rows); Assert.Null(state.NextCursor);
        await state.ObserveViewportAsync(99, false, false); Assert.Equal(4, calls);
        await state.RetryAsync(); Assert.Equal(new string?[] { null, "next", null, "next", null }, cursors);
    }

    [Fact]
    public async Task SuspendRejectsOldAccountOrLedgerReplyAndCanResumeCancelledInitialRead()
    {
        var pending = new TaskCompletionSource<ReceiptListInfo>(); var calls = 0;
        using var state = new ReceiptListState((_, _) => ++calls == 1 ? pending.Task : Task.FromResult(new ReceiptListInfo([], null)));
        var read = state.StartAsync(new("all")); state.Suspend(); pending.SetResult(new(ReceiptListStateTests.Rows(0, 1), "private")); await read;
        Assert.Empty(state.Rows); Assert.False(state.HasLoaded); Assert.Null(state.NextCursor);
        await state.ResumeAsync(); Assert.True(state.HasLoaded); Assert.Empty(state.Rows); Assert.Equal(2, calls);
        await state.ResumeAsync(); Assert.Equal(2, calls);
    }

    [Fact]
    public async Task TransportTimeoutIsAnErrorButNavigationCancellationIsNot()
    {
        using var timeout = new ReceiptListState((_, _) => Task.FromException<ReceiptListInfo>(new TaskCanceledException("synthetic timeout")));
        await timeout.StartAsync(new("all")); Assert.True(timeout.IsInitialFailure); Assert.IsType<TaskCanceledException>(timeout.Failure);
        var ready = new TaskCompletionSource();
        using var cancel = new ReceiptListState(async (_, ct) => { ready.SetResult(); await Task.Delay(Timeout.Infinite, ct); return new([], null); });
        var read = cancel.StartAsync(new("all")); await ready.Task; cancel.Suspend(); await read; Assert.Null(cancel.Failure); Assert.False(cancel.IsLoading);
    }

    [Theory]
    [InlineData("2026-12", 1, "2027-01")] [InlineData("2027-01", -1, "2026-12")]
    [InlineData("2024-02", 1, "2024-03")]
    public void MonthNavigationUsesCommittedMonthAndPreservesBothDrafts(string month, int delta, string result)
    {
        var state = new ReceiptSearchState(new(2026, 11, 10)) { MonthText = month, ShopText = "確定" };
        Assert.True(state.ApplyText()); state.MonthText = "編集中"; state.ShopText = "未確定";
        Assert.True(state.MoveMonth(delta)); Assert.Equal(result, state.Query().Month); Assert.Equal("編集中", state.MonthText); Assert.Equal("未確定", state.ShopText); Assert.Equal("確定", state.Query().Q);
        state.AllPeriods = true; state.PaymentMethodId = Guid.NewGuid(); Assert.Equal("all", state.Query().Month); Assert.Equal("確定", state.Query().Q);
        Assert.False(state.CanMove(delta));
    }
    [Theory]
    [InlineData("0001-01", -1)] [InlineData("9999-12", 1)]
    public void MonthNavigationDoesNotOverflow(string month, int delta)
    {
        var state = new ReceiptSearchState(new(2026, 11, 10)) { MonthText = month }; Assert.True(state.ApplyText()); Assert.False(state.MoveMonth(delta)); Assert.Equal(month, state.Query().Month);
    }
    [Theory]
    [InlineData(0, false)] [InlineData(100, true)] [InlineData(200, true)]
    public async Task NoContinueButtonBeforeCapOrWithoutNextCursor(int start, bool filled)
    {
        using var state = new ReceiptListState((_, _) => Task.FromResult(new ReceiptListInfo(ReceiptListStateTests.Rows(start, 100), null)));
        await state.StartAsync(new("all")); await state.ObserveViewportAsync(99, filled, false); Assert.False(state.CanContinue);
    }
}
