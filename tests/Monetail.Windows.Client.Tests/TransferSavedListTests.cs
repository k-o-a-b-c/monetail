using System.Net;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transfers;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed class TransferSavedListTests
{
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
