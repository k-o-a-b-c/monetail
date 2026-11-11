using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed class ReceiptNavigationTests
{
    private static TaskCompletionSource<ReceiptListInfo> Pending() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task InterruptedPageResumesSameRequestOnceAndIgnoresLateResponse(bool append)
    {
        var old = Pending(); var resumed = Pending(); var requests = new List<ReceiptListQuery>(); var cancelled = false;
        using var state = new ReceiptListState((q, ct) =>
        {
            requests.Add(q);
            if (append && requests.Count == 1) return Task.FromResult(new ReceiptListInfo(ReceiptListStateTests.Rows(0, 100), "next"));
            if (requests.Count == (append ? 2 : 1)) { ct.Register(() => cancelled = true); return old.Task; }
            return resumed.Task;
        });
        var query = new ReceiptListQuery("2026-11", Guid.NewGuid(), Guid.NewGuid(), "確定店名");
        var firstRead = state.StartAsync(query);
        if (append) await firstRead;
        var selected = append ? state.Rows[4].ReceiptId : (Guid?)null; state.SelectedReceiptId = selected;
        var original = state.Rows.ToArray();
        var interrupted = append ? state.ObserveViewportAsync(99, true, true) : firstRead;
        Assert.True(state.IsLoading); var notifications = new List<bool>(); state.Changed += () => notifications.Add(state.IsLoading);
        state.Suspend(); state.Suspend();
        Assert.True(cancelled); Assert.False(state.IsLoading); Assert.False(state.IsInitialLoading); Assert.False(state.IsContinuationLoading); Assert.Contains(false, notifications);
        await state.ObserveViewportAsync(99, false, true); Assert.Equal(append ? 2 : 1, requests.Count);
        var restore = state.ResumeAsync(); await state.ResumeAsync();
        Assert.True(state.IsLoading); Assert.Equal(append, state.IsContinuationLoading); Assert.Equal(!append, state.IsInitialLoading);
        Assert.Equal(append ? 3 : 2, requests.Count); Assert.Equal(requests[^2], requests[^1]);
        old.SetResult(new(ReceiptListStateTests.Rows(900, 1), "old-cursor")); await interrupted;
        Assert.Equal(original, state.Rows); Assert.Equal(selected, state.SelectedReceiptId); Assert.True(state.IsLoading);
        resumed.SetResult(new(ReceiptListStateTests.Rows(append ? 100 : 0, 1), null)); await restore;
        Assert.Equal(append ? 101 : 1, state.Rows.Count); Assert.Equal(selected, state.SelectedReceiptId); Assert.Null(state.NextCursor); Assert.False(state.IsLoading);
        await state.ResumeAsync(); Assert.Equal(append ? 3 : 2, requests.Count);
    }

    [Fact]
    public async Task ResumedAutomaticFillConsumesOnePageAndStopsAt300()
    {
        var old = Pending(); var current = Pending(); var calls = 0;
        using var state = new ReceiptListState((_, _) => ++calls switch
        { 3 => old.Task, 4 => current.Task, _ => Task.FromResult(new ReceiptListInfo(ReceiptListStateTests.Rows((calls - 1) * 100, 100), "next")) });
        await state.StartAsync(new("all")); await state.ObserveViewportAsync(99, false, false);
        var interrupted = state.ObserveViewportAsync(199, false, false); state.Suspend();
        var restore = state.ResumeAsync(); await state.ResumeAsync(); Assert.Equal(4, calls);
        current.SetResult(new(ReceiptListStateTests.Rows(200, 100), "after300")); await restore;
        old.SetResult(new(ReceiptListStateTests.Rows(900, 100), "old")); await interrupted;
        Assert.Equal(300, state.Rows.Count); Assert.Equal(2, state.AutomaticFillCount); Assert.Equal("after300", state.NextCursor); Assert.True(state.CanContinue);
        await state.ObserveViewportAsync(299, false, false); await state.ResumeAsync(); Assert.Equal(4, calls);
        await state.ContinueAsync(); Assert.Equal(400, state.Rows.Count); Assert.Equal(5, calls);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task FailureOfResumedReadRequiresManualRetryIncludingAnotherScreenRoundTrip(bool append)
    {
        var pending = Pending(); var calls = 0;
        using var state = new ReceiptListState((_, ct) =>
        {
            calls++;
            if (append && calls == 1) return Task.FromResult(new ReceiptListInfo(ReceiptListStateTests.Rows(0, 100), "next"));
            if (calls == (append ? 2 : 1)) return pending.Task.WaitAsync(ct);
            if (calls == (append ? 3 : 2)) return Task.FromException<ReceiptListInfo>(new HttpRequestException());
            return Task.FromResult(new ReceiptListInfo([], null));
        });
        var first = state.StartAsync(new("all")); if (append) await first;
        var interrupted = append ? state.ObserveViewportAsync(99, true, true) : first;
        state.Suspend(); await interrupted; await state.ResumeAsync(); Assert.NotNull(state.Failure);
        var failedCalls = calls; state.Suspend(); await state.ResumeAsync(); await state.ObserveViewportAsync(99, false, true);
        Assert.Equal(failedCalls, calls); Assert.Equal(append, state.IsContinuationFailure); Assert.Equal(!append, state.IsInitialFailure);
        await state.RetryAsync(); Assert.Equal(failedCalls + 1, calls); Assert.Null(state.Failure);
    }

    [Fact]
    public async Task NewSearchDiscardsTheInterruptedRequest()
    {
        var pending = Pending(); var requests = new List<ReceiptListQuery>();
        using var state = new ReceiptListState((q, ct) => { requests.Add(q); return q.Q == "old" ? pending.Task.WaitAsync(ct) : Task.FromResult(new ReceiptListInfo([], null)); });
        var read = state.StartAsync(new("all", q: "old")); state.Suspend(); await read;
        await state.StartAsync(new("2026-11", q: "new")); await state.ResumeAsync();
        Assert.Equal(new[] { "old", "new" }, requests.Select(q => q.Q));
    }
}
