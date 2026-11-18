using System.Net;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transfers;
using Monetail.Windows.Client.Transport;
using Xunit;
using Monetail.Windows.Presentation.Transfers;
using static Monetail.Windows.Presentation.Tests.TransferListViewModelTests;

namespace Monetail.Windows.Presentation.Tests;

public sealed class TransferPagingTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(100)] [InlineData(101)] [InlineData(201)]
    public async Task PagesPreserveApiOrderAndAppliedConditions(int count)
    {
        var queries = new List<TransferListQuery>(); var owner = Guid.NewGuid(); var payment = Guid.NewGuid();
        using var state = Create((q, _) =>
        {
            queries.Add(q); var offset = q.Cursor is null ? 0 : int.Parse(q.Cursor);
            return Task.FromResult(new TransferListInfo(TransferListViewModelTests.Rows(offset, Math.Min(100, count - offset)), offset + 100 < count ? (offset + 100).ToString() : null));
        });
        await state.StartAsync(new("2026-11", owner, payment, "店舗"));
        while (state.NextCursor is not null) await state.ObserveViewportAsync(state.Rows.Count - 1, true, true);
        Assert.Equal(count, state.Rows.Count); Assert.Equal(TransferListViewModelTests.Rows(0, count), state.Rows.Select(r => r.Transfer));
        Assert.All(queries, q => { Assert.Equal("2026-11", q.Month); Assert.Equal(owner, q.OwnerPersonId); Assert.Equal(payment, q.PaymentMethodId); Assert.Equal("店舗", q.Q); });
        Assert.Equal(Enumerable.Range(1, Math.Max(1, (count + 99) / 100)).Select(i => i == 1 ? null : ((i - 1) * 100).ToString()), queries.Select(q => q.Cursor));
    }

    [Fact]
    public async Task PrefetchBoundaryAndConcurrentNotificationsIssueOnlyOneRequest()
    {
        var pending = new TaskCompletionSource<TransferListInfo>(); var calls = 0;
        using var state = Create((_, _) => ++calls == 1 ? Task.FromResult(new TransferListInfo(TransferListViewModelTests.Rows(0, 100), "next")) : pending.Task);
        await state.StartAsync(new("all"));
        await state.ObserveViewportAsync(78, true, true); Assert.Equal(1, calls);
        var read = state.ObserveViewportAsync(79, true, true); Assert.True(state.IsContinuationLoading);
        await state.ObserveViewportAsync(99, true, true); await state.ContinueAsync(); await state.RetryAsync(); Assert.Equal(2, calls);
        pending.SetResult(new(TransferListViewModelTests.Rows(100, 1), null)); await read;
        Assert.Equal(101, state.Rows.Count); Assert.False(state.CanContinue);
    }

    [Fact]
    public async Task ErrorsRequireManualRetryAndKeepAppendedRowsSelectionAndCursor()
    {
        var calls = 0; var cursors = new List<string?>(); var offline = new HttpRequestException();
        using var state = Create((q, _) =>
        {
            cursors.Add(q.Cursor); calls++;
            return calls switch { 1 or 3 => Task.FromException<TransferListInfo>(offline), 2 => Task.FromResult(new TransferListInfo(TransferListViewModelTests.Rows(0, 100), "next")), _ => Task.FromResult(new TransferListInfo(TransferListViewModelTests.Rows(100, 1), null)) };
        });
        await state.StartAsync(new("all")); Assert.True(state.IsInitialFailure); Assert.Empty(state.Rows);
        await state.ObserveViewportAsync(0, false, false); Assert.Equal(1, calls);
        await state.RetryAsync(); var selected = state.Rows[0].TransferId; state.SelectedItem = state.Rows.FirstOrDefault(r => r.TransferId == selected);
        await state.ObserveViewportAsync(99, true, true); Assert.True(state.IsContinuationFailure); Assert.Equal(100, state.Rows.Count); Assert.Equal(selected, state.SelectedTransferId);
        await state.ObserveViewportAsync(99, true, true); Assert.Equal(3, calls);
        await state.RetryAsync(); Assert.Equal(101, state.Rows.Count); Assert.Equal(selected, state.SelectedTransferId);
        Assert.Equal(new string?[] { null, null, "next", "next" }, cursors);
    }

    [Fact]
    public async Task InvalidCursorRestartsOnceThenStopsAndManualRetryNeverResendsRejectedCursor()
    {
        var cursors = new List<string?>(); var calls = 0;
        using var state = Create((q, _) =>
        {
            cursors.Add(q.Cursor); calls++;
            return q.Cursor is not null ? Task.FromException<TransferListInfo>(new TransferRequestException(HttpStatusCode.BadRequest, "invalid_cursor", null))
                : Task.FromResult(new TransferListInfo(TransferListViewModelTests.Rows(calls * 100, 100), "next"));
        });
        await state.StartAsync(new("all")); state.SelectedItem = state.Rows.FirstOrDefault(r => r.TransferId == state.Rows[0].TransferId);
        await state.ObserveViewportAsync(99, true, true); Assert.Equal(3, calls); Assert.Equal(100, state.Rows.Count); Assert.Null(state.SelectedTransferId);
        await state.ObserveViewportAsync(99, true, true); Assert.Equal(4, calls); Assert.True(state.IsInitialFailure); Assert.Empty(state.Rows); Assert.Null(state.NextCursor);
        await state.ObserveViewportAsync(99, false, false); Assert.Equal(4, calls);
        await state.RetryAsync(); Assert.Equal(new string?[] { null, "next", null, "next", null }, cursors);
    }

    [Fact]
    public async Task SuspendRejectsOldAccountOrLedgerReplyAndCanResumeCancelledInitialRead()
    {
        var pending = new TaskCompletionSource<TransferListInfo>(); var calls = 0;
        using var state = Create((_, _) => ++calls == 1 ? pending.Task : Task.FromResult(new TransferListInfo([], null)));
        var read = state.StartAsync(new("all")); state.Suspend(); pending.SetResult(new(TransferListViewModelTests.Rows(0, 1), "private")); await read;
        Assert.Empty(state.Rows); Assert.False(state.HasLoaded); Assert.Null(state.NextCursor);
        await state.ResumeAsync(); Assert.True(state.HasLoaded); Assert.Empty(state.Rows); Assert.Equal(2, calls);
        await state.ResumeAsync(); Assert.Equal(2, calls);
    }

    [Fact]
    public async Task NavigationDuringAutomaticFillDoesNotExposeContinueBefore300OrPreventResumption()
    {
        var pending = new TaskCompletionSource<TransferListInfo>(); var calls = 0;
        using var state = Create((_, _) => ++calls == 3 ? pending.Task : Task.FromResult(new TransferListInfo(TransferListViewModelTests.Rows((calls - 1) * 100, 100), "next")));
        await state.StartAsync(new("all")); await state.ObserveViewportAsync(99, false, false);
        var read = state.ObserveViewportAsync(199, false, false); state.Suspend(); pending.SetResult(new(TransferListViewModelTests.Rows(200, 100), "old")); await read;
        Assert.Equal(200, state.Rows.Count); Assert.Equal(1, state.AutomaticFillCount); Assert.False(state.CanContinue);
        await state.ResumeAsync(); await state.ObserveViewportAsync(199, false, false);
        Assert.Equal(300, state.Rows.Count); Assert.Equal(2, state.AutomaticFillCount); Assert.True(state.CanContinue); Assert.Equal(4, calls);
    }

    [Fact]
    public async Task CursorRecoveryStartsANewAutomaticFillBudget()
    {
        var calls = 0;
        using var state = Create((_, _) => ++calls == 2
            ? Task.FromException<TransferListInfo>(new TransferRequestException(HttpStatusCode.BadRequest, "invalid_cursor", null))
            : Task.FromResult(new TransferListInfo(TransferListViewModelTests.Rows(calls * 100, 100), "next")));
        await state.StartAsync(new("all")); await state.ObserveViewportAsync(99, false, false);
        Assert.Equal(100, state.Rows.Count); Assert.Equal(0, state.AutomaticFillCount);
        await state.ObserveViewportAsync(99, false, false); await state.ObserveViewportAsync(199, false, false);
        Assert.Equal(300, state.Rows.Count); Assert.Equal(2, state.AutomaticFillCount); Assert.True(state.CanContinue);
        await state.ObserveViewportAsync(299, false, false); Assert.Equal(5, calls);
    }

    [Fact]
    public async Task TransportTimeoutIsAnErrorButNavigationCancellationIsNot()
    {
        using var timeout = Create((_, _) => Task.FromException<TransferListInfo>(new TaskCanceledException("synthetic timeout")));
        await timeout.StartAsync(new("all")); Assert.True(timeout.IsInitialFailure); Assert.IsType<TaskCanceledException>(timeout.Failure);
        var ready = new TaskCompletionSource();
        using var cancel = Create(async (_, ct) => { ready.SetResult(); await Task.Delay(Timeout.Infinite, ct); return new([], null); });
        var read = cancel.StartAsync(new("all")); await ready.Task; cancel.Suspend(); await read; Assert.Null(cancel.Failure); Assert.False(cancel.IsLoading);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(100, true)] [InlineData(200, true)]
    public async Task NoContinueButtonBeforeCapOrWithoutNextCursor(int start, bool filled)
    {
        using var state = Create((_, _) => Task.FromResult(new TransferListInfo(TransferListViewModelTests.Rows(start, 100), null)));
        await state.StartAsync(new("all")); await state.ObserveViewportAsync(99, filled, false); Assert.False(state.CanContinue);
    }
}
