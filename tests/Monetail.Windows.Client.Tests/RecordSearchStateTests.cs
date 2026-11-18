using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transfers;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed partial class RecordSearchStateTests
{
    [Fact]
    public void RecordSearchSharesFiltersButKeepsBothPendingNamesIndependent()
    {
        var state = new RecordSearchState(new(2026, 12, 31)); var owner = Guid.NewGuid(); var payment = Guid.NewGuid();
        state.Receipts.MonthText = "2027-01"; state.Receipts.ShopText = "未確定店"; state.TransferText = "未確定振替";
        state.Receipts.OwnerPersonId = owner; state.Receipts.PaymentMethodId = payment;
        Assert.Equal("2026-12", state.TransferQuery().Month); Assert.Equal("", state.TransferQuery().Q); Assert.Equal("", state.Receipts.Query().Q);
        Assert.True(state.Receipts.ApplyText()); Assert.Equal("2027-01", state.TransferQuery().Month); Assert.Equal("未確定店", state.Receipts.Query().Q); Assert.Equal("", state.TransferQuery().Q);
        Assert.True(state.ApplyTransferText()); Assert.Equal("未確定振替", state.TransferQuery().Q); Assert.Equal(owner, state.TransferQuery().OwnerPersonId); Assert.Equal(payment, state.TransferQuery().PaymentMethodId);
    }

    [Fact]
    public void RecordMonthOnlyApplyAndImmediateChoicesNeverCommitEitherName()
    {
        var state = new RecordSearchState(new(2026, 12, 31)); state.Receipts.ShopText = "未確定店"; state.TransferText = "未確定振替";
        state.Receipts.MonthText = "2027-01"; Assert.True(state.Receipts.ApplyMonthText());
        Assert.Equal("", state.Receipts.Query().Q); Assert.Equal("", state.TransferQuery().Q); Assert.Equal("2027-01", state.TransferQuery().Month);
        var payment = Guid.NewGuid(); state.Receipts.PaymentMethodId = payment; state.Receipts.OwnerPersonId = Guid.NewGuid();
        Assert.Equal(payment, state.TransferQuery().PaymentMethodId); Assert.Equal(payment, state.Receipts.Query().PaymentMethodId);
        Assert.True(state.Receipts.MoveMonth(-1)); Assert.Equal("2026-12", state.TransferQuery().Month); Assert.Equal("未確定振替", state.TransferText);
    }

    [Fact]
    public void RecordInvalidMonthIsAtomicAndAllPeriodsKeepsPreviousMonthAndSelections()
    {
        var state = new RecordSearchState(new(2026, 12, 31)); state.TransferText = "銀行"; Assert.True(state.ApplyTransferText());
        var owner = Guid.NewGuid(); var payment = Guid.NewGuid(); state.Receipts.OwnerPersonId = owner; state.Receipts.PaymentMethodId = payment;
        state.Receipts.MonthText = "2027-13"; state.TransferText = "未確定"; Assert.False(state.ApplyTransferText());
        Assert.Equal("銀行", state.TransferQuery().Q); Assert.Equal("2026-12", state.TransferQuery().Month); Assert.NotNull(state.Receipts.MonthError);
        state.Receipts.AllPeriods = true; Assert.Equal("all", state.TransferQuery().Month); Assert.False(state.Receipts.MoveMonth(1));
        state.Receipts.AllPeriods = false; Assert.Equal("2026-12", state.TransferQuery().Month); Assert.Equal(owner, state.TransferQuery().OwnerPersonId); Assert.Equal(payment, state.TransferQuery().PaymentMethodId);
    }


}
