using System.Net;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transfers;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed class TransferSavedListTests
{
    [Fact]
    public async Task SavedOutside300UsesFocusAndItsCursorThenCanReturnToStart()
    {
        var calls = new List<TransferListQuery>(); var saved = TransferListStateTests.Rows(500, 1)[0];
        using var state = new TransferListState((q, _) =>
        {
            calls.Add(q);
            return Task.FromResult(q.FocusTransferId is not null ? new TransferListInfo([saved], "focused") : new TransferListInfo(TransferListStateTests.Rows(calls.Count * 100, 100), "next"));
        });
        await state.ShowSavedAsync(new("2026-11", q: "銀行"), saved.TransferId);
        Assert.Equal(4, calls.Count); Assert.Equal(saved.TransferId, calls[^1].FocusTransferId); Assert.Equal(saved.TransferId, state.SelectedTransferId);
        await state.ObserveViewportAsync(0, true, true); Assert.Equal("focused", calls[^1].Cursor); Assert.Null(calls[^1].FocusTransferId);
        await state.ReturnToStartAsync(); Assert.Null(state.FocusTransferId); Assert.Null(calls[^1].Cursor); Assert.Equal("銀行", calls[^1].Q);
    }
    [Fact]
    public void SavingAdjustsOnlyMismatchedConditionsAndPreservesShopDraft()
    {
        var state = new RecordSearchState(new(2026, 11, 1)); var from = Guid.NewGuid(); var to = Guid.NewGuid(); var owner = Guid.NewGuid();
        state.Receipts.ShopText = "未確定店"; state.TransferText = "atm"; Assert.True(state.ApplyTransferText());
        state.Receipts.AllPeriods = true; state.Receipts.OwnerPersonId = owner; state.Receipts.PaymentMethodId = to;
        var methods = new PaymentMethodsInfo([new(from, owner, "現金", 0, null, null, Guid.NewGuid()), new(to, Guid.NewGuid(), "銀行", 0, null, null, Guid.NewGuid())], [], null, "Admin");
        state.ApplySavedTransfer(new(new(2027, 1, 1), "ＡＴＭ入金", from, to, 10, null), methods);
        Assert.Equal("2027-01", state.TransferQuery().Month); Assert.Equal(owner, state.TransferQuery().OwnerPersonId); Assert.Equal(to, state.TransferQuery().PaymentMethodId);
        Assert.Equal("atm", state.TransferQuery().Q); Assert.Equal("未確定店", state.Receipts.ShopText); Assert.Equal("", state.Receipts.Query().Q);
    }
    [Theory]
    [InlineData(1, 1)] [InlineData(99, 1)] [InlineData(100, 1)] [InlineData(101, 2)] [InlineData(201, 3)] [InlineData(300, 3)]
    public async Task SavedWithinFirst300SelectsItWithoutFocus(int savedRow, int pages)
    {
        var queries = new List<TransferListQuery>();
        using var state = new TransferListState((q, _) => { queries.Add(q); var offset = q.Cursor is null ? 0 : int.Parse(q.Cursor); return Task.FromResult(new TransferListInfo(TransferListStateTests.Rows(offset, 100), (offset + 100).ToString())); });
        var id = TransferListStateTests.Rows(savedRow - 1, 1)[0].TransferId;
        await state.ShowSavedAsync(new("all"), id);
        Assert.Equal(pages, queries.Count); Assert.All(queries, q => Assert.Null(q.FocusTransferId)); Assert.Equal(id, state.SelectedTransferId);
    }
    [Fact]
    public async Task Focus404RecoversOnceAndNeverTreatsNormal404AsAnotherFocusFailure()
    {
        var queries = new List<TransferListQuery>(); var owner = Guid.NewGuid(); var payment = Guid.NewGuid();
        using var state = new TransferListState((q, _) => { queries.Add(q); throw new TransferRequestException(HttpStatusCode.NotFound, "not_found", null); });
        await state.StartAsync(new("2026-11", owner, payment, "保持", focusTransferId: Guid.NewGuid()));
        Assert.Equal(2, queries.Count); Assert.NotNull(queries[0].FocusTransferId); Assert.Null(queries[1].FocusTransferId); Assert.Null(state.FocusTransferId);
        Assert.Equal(owner, queries[1].OwnerPersonId); Assert.Equal(payment, queries[1].PaymentMethodId); Assert.Equal("保持", queries[1].Q);
        Assert.True(state.IsInitialFailure); await state.ObserveViewportAsync(0, false, true); Assert.Equal(2, queries.Count);
    }
    [Fact]
    public async Task FocusInvalidCursorReturnsToRealStartAndResetsSelection()
    {
        var id = Guid.NewGuid(); var calls = new List<TransferListQuery>();
        using var state = new TransferListState((q, _) =>
        {
            calls.Add(q); if (q.Cursor is not null) throw new TransferRequestException(HttpStatusCode.BadRequest, "invalid_cursor", "cursor");
            return Task.FromResult(new TransferListInfo(TransferListStateTests.Rows(0, 100), "next"));
        });
        await state.StartAsync(new("all", focusTransferId: id)); state.SelectedTransferId = id; await state.ObserveViewportAsync(99, true, true);
        Assert.Equal(3, calls.Count); Assert.Null(calls[^1].FocusTransferId); Assert.Null(state.FocusTransferId); Assert.Null(state.SelectedTransferId);
    }
    [Fact]
    public async Task NewSearchDiscardsDelayedFocusResultAndSelection()
    {
        var pending = new TaskCompletionSource<TransferListInfo>(TaskCreationOptions.RunContinuationsAsynchronously); var id = Guid.NewGuid();
        using var state = new TransferListState((q, _) => q.FocusTransferId is not null ? pending.Task : Task.FromResult(new TransferListInfo([], null)));
        var previous = state.ShowSavedAsync(new("all"), id); await state.StartAsync(new("2026-12"));
        pending.SetResult(new([TransferListStateTests.Rows(0, 1)[0] with { TransferId = id }], null)); await previous;
        Assert.Empty(state.Rows); Assert.Null(state.SelectedTransferId); Assert.Null(state.FocusTransferId);
    }
    [Theory]
    [InlineData("ATM", "ａｔｍ", true)] [InlineData("ＡＴＭ１", "atm1", true)]
    [InlineData("カナ", "かな", false)] [InlineData("①", "1", false)] [InlineData("Ａ－Ｂ", "a-b", false)] [InlineData(" 銀行 ", " 銀行 ", true)]
    public void SavedNameFilterMatchesOnlyApiApprovedCharacters(string name, string query, bool expected) => Assert.Equal(expected, TransferEditorRules.NameMatches(name, query));
    [Fact]
    public void SavingClearsOnlyTheMismatchedOwnerPaymentAndNameButNeverShop()
    {
        var state = new RecordSearchState(new(2026, 11, 1)); state.Receipts.ShopText = "確定店"; Assert.True(state.Receipts.ApplyText()); state.Receipts.ShopText = "店の下書き";
        state.TransferText = "ATM"; Assert.True(state.ApplyTransferText()); state.Receipts.OwnerPersonId = Guid.NewGuid(); state.Receipts.PaymentMethodId = Guid.NewGuid();
        var from = Guid.NewGuid(); var to = Guid.NewGuid(); var owner = Guid.NewGuid(); var methods = new PaymentMethodsInfo([new(from, owner, "現金", 0, null, null, Guid.NewGuid()), new(to, owner, "銀行", 1, null, null, Guid.NewGuid())], [], owner, "Admin");
        state.ApplySavedTransfer(new(new(2027, 2, 1), "振替", from, to, 100, null), methods);
        Assert.Null(state.TransferQuery().OwnerPersonId); Assert.Null(state.TransferQuery().PaymentMethodId); Assert.Equal("", state.TransferQuery().Q); Assert.Equal("", state.TransferText);
        Assert.Equal("確定店", state.Receipts.Query().Q); Assert.Equal("店の下書き", state.Receipts.ShopText); Assert.Equal("2027-02", state.Receipts.Query().Month);
    }
}
