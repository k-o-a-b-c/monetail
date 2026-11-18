using System.Net;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transfers;
using Monetail.Windows.Client.Transport;
using Xunit;
using Monetail.Windows.Presentation.Transfers;
using static Monetail.Windows.Presentation.Tests.TransferListViewModelTests;

namespace Monetail.Windows.Presentation.Tests;

public sealed class TransferSavedListTests
{
    [Fact]
    public async Task SavedOutside300UsesFocusAndItsCursorThenCanReturnToStart()
    {
        var calls = new List<TransferListQuery>(); var saved = TransferListViewModelTests.Rows(500, 1)[0];
        using var state = Create((q, _) =>
        {
            calls.Add(q);
            return Task.FromResult(q.FocusTransferId is not null ? new TransferListInfo([saved], "focused") : new TransferListInfo(TransferListViewModelTests.Rows(calls.Count * 100, 100), "next"));
        });
        await state.ShowSavedAsync(new("2026-11", q: "銀行"), saved.TransferId);
        Assert.Equal(4, calls.Count); Assert.Equal(saved.TransferId, calls[^1].FocusTransferId); Assert.Equal(saved.TransferId, state.SelectedTransferId);
        await state.ObserveViewportAsync(0, true, true); Assert.Equal("focused", calls[^1].Cursor); Assert.Null(calls[^1].FocusTransferId);
        await state.ReturnToStartAsync(); Assert.Null(state.FocusTransferId); Assert.Null(calls[^1].Cursor); Assert.Equal("銀行", calls[^1].Q);
    }
    [Theory]
    [InlineData(1, 1)] [InlineData(99, 1)] [InlineData(100, 1)] [InlineData(101, 2)] [InlineData(201, 3)] [InlineData(300, 3)]
    public async Task SavedWithinFirst300SelectsItWithoutFocus(int savedRow, int pages)
    {
        var queries = new List<TransferListQuery>();
        using var state = Create((q, _) => { queries.Add(q); var offset = q.Cursor is null ? 0 : int.Parse(q.Cursor); return Task.FromResult(new TransferListInfo(TransferListViewModelTests.Rows(offset, 100), (offset + 100).ToString())); });
        var id = TransferListViewModelTests.Rows(savedRow - 1, 1)[0].TransferId;
        await state.ShowSavedAsync(new("all"), id);
        Assert.Equal(pages, queries.Count); Assert.All(queries, q => Assert.Null(q.FocusTransferId)); Assert.Equal(id, state.SelectedTransferId);
    }
    [Fact]
    public async Task Focus404RecoversOnceAndNeverTreatsNormal404AsAnotherFocusFailure()
    {
        var queries = new List<TransferListQuery>(); var owner = Guid.NewGuid(); var payment = Guid.NewGuid();
        using var state = Create((q, _) => { queries.Add(q); throw new TransferRequestException(HttpStatusCode.NotFound, "not_found", null); });
        await state.StartAsync(new("2026-11", owner, payment, "保持", focusTransferId: Guid.NewGuid()));
        Assert.Equal(2, queries.Count); Assert.NotNull(queries[0].FocusTransferId); Assert.Null(queries[1].FocusTransferId); Assert.Null(state.FocusTransferId);
        Assert.Equal(owner, queries[1].OwnerPersonId); Assert.Equal(payment, queries[1].PaymentMethodId); Assert.Equal("保持", queries[1].Q);
        Assert.True(state.IsInitialFailure); await state.ObserveViewportAsync(0, false, true); Assert.Equal(2, queries.Count);
    }
    [Fact]
    public async Task FocusInvalidCursorReturnsToRealStartAndResetsSelection()
    {
        var id = Guid.NewGuid(); var calls = new List<TransferListQuery>();
        using var state = Create((q, _) =>
        {
            calls.Add(q); if (q.Cursor is not null) throw new TransferRequestException(HttpStatusCode.BadRequest, "invalid_cursor", "cursor");
            return Task.FromResult(new TransferListInfo(TransferListViewModelTests.Rows(0, 100), "next"));
        });
        await state.StartAsync(new("all", focusTransferId: id)); state.SelectedItem = state.Rows.FirstOrDefault(r => r.TransferId == id); await state.ObserveViewportAsync(99, true, true);
        Assert.Equal(3, calls.Count); Assert.Null(calls[^1].FocusTransferId); Assert.Null(state.FocusTransferId); Assert.Null(state.SelectedTransferId);
    }
    [Fact]
    public async Task NewSearchDiscardsDelayedFocusResultAndSelection()
    {
        var pending = new TaskCompletionSource<TransferListInfo>(TaskCreationOptions.RunContinuationsAsynchronously); var id = Guid.NewGuid();
        using var state = Create((q, _) => q.FocusTransferId is not null ? pending.Task : Task.FromResult(new TransferListInfo([], null)));
        var previous = state.ShowSavedAsync(new("all"), id); await state.StartAsync(new("2026-12"));
        pending.SetResult(new([TransferListViewModelTests.Rows(0, 1)[0] with { TransferId = id }], null)); await previous;
        Assert.Empty(state.Rows); Assert.Null(state.SelectedTransferId); Assert.Null(state.FocusTransferId);
    }

}
