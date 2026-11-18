using System.Net;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Client.Transport;
using Xunit;
using Monetail.Windows.Presentation.Receipts;
using static Monetail.Windows.Presentation.Tests.ReceiptListViewModelTests;

namespace Monetail.Windows.Presentation.Tests;

public sealed class ReceiptSavedListTests
{
    [Fact]
    public async Task SavedOutside300UsesFocusAndItsCursorThenCanReturnToStart()
    {
        var calls = new List<ReceiptListQuery>();
        var saved = ReceiptListViewModelTests.Rows(500, 1)[0];
        using var state = Create((q, _) =>
        {
            calls.Add(q);
            return Task.FromResult(q.FocusReceiptId is not null ? new ReceiptListInfo([saved], "focused") : new ReceiptListInfo(ReceiptListViewModelTests.Rows(calls.Count * 100, 100), "next"));
        });
        await state.ShowSavedAsync(new("2026-11", q: "銀行"), saved.ReceiptId);
        Assert.Equal(4, calls.Count);
        Assert.Equal(saved.ReceiptId, calls[^1].FocusReceiptId);
        Assert.Equal(saved.ReceiptId, state.SelectedReceiptId);
        await state.ObserveViewportAsync(0, true, true);
        Assert.Equal("focused", calls[^1].Cursor);
        Assert.Null(calls[^1].FocusReceiptId);
        await state.ReturnToStartAsync();
        Assert.Null(state.FocusReceiptId);
        Assert.Null(calls[^1].Cursor);
        Assert.Equal("銀行", calls[^1].Q);
    }
    [Theory]
    [InlineData(1, 1)]
    [InlineData(99, 1)]
    [InlineData(100, 1)]
    [InlineData(101, 2)]
    [InlineData(201, 3)]
    [InlineData(300, 3)]
    public async Task SavedWithinFirst300SelectsItWithoutFocus(int savedRow, int pages)
    {
        var queries = new List<ReceiptListQuery>();
        using var state = Create((q, _) => { queries.Add(q); var offset = q.Cursor is null ? 0 : int.Parse(q.Cursor); return Task.FromResult(new ReceiptListInfo(ReceiptListViewModelTests.Rows(offset, 100), (offset + 100).ToString())); });
        var id = ReceiptListViewModelTests.Rows(savedRow - 1, 1)[0].ReceiptId;
        await state.ShowSavedAsync(new("all"), id);
        Assert.Equal(pages, queries.Count);
        Assert.All(queries, q => Assert.Null(q.FocusReceiptId));
        Assert.Equal(id, state.SelectedReceiptId);
    }
    [Fact]
    public async Task Focus404RecoversOnceAndNeverTreatsNormal404AsAnotherFocusFailure()
    {
        var queries = new List<ReceiptListQuery>();
        var owner = Guid.NewGuid();
        var payment = Guid.NewGuid();
        using var state = Create((q, _) => { queries.Add(q); throw new ReceiptRequestException(HttpStatusCode.NotFound, "not_found", null); });
        await state.StartAsync(new("2026-11", owner, payment, "保持", focusReceiptId: Guid.NewGuid()));
        Assert.Equal(2, queries.Count);
        Assert.NotNull(queries[0].FocusReceiptId);
        Assert.Null(queries[1].FocusReceiptId);
        Assert.Null(state.FocusReceiptId);
        Assert.Equal(owner, queries[1].OwnerPersonId);
        Assert.Equal(payment, queries[1].PaymentMethodId);
        Assert.Equal("保持", queries[1].Q);
        Assert.True(state.IsInitialFailure);
        await state.ObserveViewportAsync(0, false, true);
        Assert.Equal(2, queries.Count);
    }
    [Fact]
    public async Task FocusInvalidCursorReturnsToRealStartAndResetsSelection()
    {
        var id = Guid.NewGuid();
        var calls = new List<ReceiptListQuery>();
        using var state = Create((q, _) =>
        {
            calls.Add(q);
            if (q.Cursor is not null)
                throw new ReceiptRequestException(HttpStatusCode.BadRequest, "invalid_cursor", "cursor");
            return Task.FromResult(new ReceiptListInfo(ReceiptListViewModelTests.Rows(0, 100), "next"));
        });
        await state.StartAsync(new("all", focusReceiptId: id));
        state.SelectedItem = state.Rows[0];
        Assert.NotNull(state.SelectedReceiptId);
        await state.ObserveViewportAsync(99, true, true);
        Assert.Equal(3, calls.Count);
        Assert.Null(calls[^1].FocusReceiptId);
        Assert.Null(state.FocusReceiptId);
        Assert.Null(state.SelectedReceiptId);
    }
    [Fact]
    public async Task NewSearchDiscardsDelayedFocusResultAndSelection()
    {
        var pending = new TaskCompletionSource<ReceiptListInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        var id = Guid.NewGuid();
        using var state = Create((q, _) => q.FocusReceiptId is not null ? pending.Task : Task.FromResult(new ReceiptListInfo([], null)));
        var previous = state.ShowSavedAsync(new("all"), id);
        await state.StartAsync(new("2026-12"));
        pending.SetResult(new([ReceiptListViewModelTests.Rows(0, 1)[0] with { ReceiptId = id }], null));
        await previous;
        Assert.Empty(state.Rows);
        Assert.Null(state.SelectedReceiptId);
        Assert.Null(state.FocusReceiptId);
    }

    [Fact]
    public async Task FailedSavedDiscoveryCanBeExplicitlyRetriedWithoutAnOutsideSavedSelectionField()
    {
        var calls = 0;
        var saved = Rows(100, 1)[0];
        using var model = Create((q, _) => ++calls == 2 ? Task.FromException<ReceiptListInfo>(new HttpRequestException())
            : Task.FromResult(q.Cursor is null ? new ReceiptListInfo(Rows(0, 100), "next") : new ReceiptListInfo([saved], null)));
        await model.ShowSavedAsync(new("all"), saved.ReceiptId);
        Assert.True(model.IsContinuationFailure);
        Assert.False(model.IsFindingSaved);
        await model.RetryCommand.ExecuteAsync(null);
        Assert.Equal(saved.ReceiptId, model.SelectedItem?.ReceiptId);
        Assert.False(model.IsFindingSaved);
        Assert.Equal(4, calls);
    }
    [Fact]
    public async Task SavedDiscoveryResumesTheInterruptedCursorAndKeepsTheThreePageBudget()
    {
        var queries = new List<ReceiptListQuery>();
        var pending = new TaskCompletionSource<ReceiptListInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var model = Create((q, _) =>
        {
            queries.Add(q);
            return queries.Count == 2 ? pending.Task
                : Task.FromResult(new ReceiptListInfo(Rows(q.Cursor is null ? 0 : 100, 100), "next"));
        });
        var id = Rows(100, 1)[0].ReceiptId;
        var showing = model.ShowSavedAsync(new("all"), id);
        model.Suspend();
        pending.SetResult(new(Rows(100, 100), "stale"));
        await showing;
        Assert.Equal(100, model.Rows.Count);
        await model.ResumeAsync();
        Assert.Equal("next", queries[^1].Cursor);
        Assert.Equal(id, model.SelectedReceiptId);
        Assert.False(model.IsFindingSaved);
        Assert.Equal(1, model.AutomaticFillCount);
        Assert.Equal(3, queries.Count);
    }
    [Fact]
    public async Task ResumedFocus404FallsBackOnceEvenWhenTheReplacementRequestCompletesSynchronously()
    {
        var queries = new List<ReceiptListQuery>();
        var pending = new TaskCompletionSource<ReceiptListInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        var target = Rows(500, 1)[0].ReceiptId;
        using var model = Create((q, _) =>
        {
            queries.Add(q);
            if (queries.Count == 4)
                return pending.Task;
            if (q.FocusReceiptId is not null)
                throw new ReceiptRequestException(HttpStatusCode.NotFound, "not_found", null);
            return Task.FromResult(queries.Count > 4 ? new ReceiptListInfo([], null)
                : new ReceiptListInfo(Rows((queries.Count - 1) * 100, 100), "next"));
        });
        var showing = model.ShowSavedAsync(new("all"), target);
        model.Suspend();
        pending.SetException(new ReceiptRequestException(HttpStatusCode.NotFound, "not_found", null));
        await showing;
        await model.ResumeAsync();
        Assert.Equal(6, queries.Count);
        Assert.Equal(2, queries.Count(q => q.FocusReceiptId == target));
        Assert.Null(model.FocusReceiptId);
        Assert.Null(model.Failure);
        Assert.Null(model.SelectedItem);
        Assert.False(model.IsFindingSaved);
        Assert.Empty(model.Rows);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthenticationFailureDuringSavedReadStopsAndRequiresExplicitRetry(bool focus)
    {
        var calls = 0;
        var failure = new Monetail.Windows.Client.Authentication.AuthenticationRejectedException();
        var saved = ReceiptListViewModelTests.Rows(500, 1)[0];
        using var model = Create((q, _) =>
        {
            calls++;
            if (calls == (focus ? 4 : 2))
                throw failure;
            return Task.FromResult(q.FocusReceiptId is not null || calls > 4
                ? new ReceiptListInfo([saved], null) : new ReceiptListInfo(ReceiptListViewModelTests.Rows((calls - 1) * 100, 100), "next"));
        });
        await model.ShowSavedAsync(new("all"), saved.ReceiptId);
        Assert.Same(failure, model.Failure);
        Assert.False(model.IsFindingSaved);
        var failedCalls = calls;
        model.Suspend();
        await model.ResumeAsync();
        Assert.Equal(failedCalls, calls);
        await model.RetryAsync();
        Assert.Null(model.Failure);
        Assert.Equal(saved.ReceiptId, model.SelectedReceiptId);
    }
}
