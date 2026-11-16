using Monetail.Windows.Client.Transport;
using Monetail.Windows.Client.Receipts;

namespace Monetail.Windows.Client.Ledgers;

/// <summary>年月・人物・支払方法だけを共用し、店名/振替名の下書きと確定を混同しない。</summary>
public sealed class RecordSearchState(DateOnly today)
{
    public ReceiptSearchState Receipts { get; } = new(today);
    public string TransferText { get; set; } = "";
    public string AppliedTransferName { get; private set; } = "";
    public bool ApplyTransferText()
    {
        if (!Receipts.ApplyMonthText()) return false;
        AppliedTransferName = TransferText; return true;
    }
    public void ApplySavedTransfer(TransferSnapshot saved, PaymentMethodsInfo methods)
    {
        Receipts.SelectMonth(DateOnly.FromDateTime(saved.TransactionAt));
        var owners = methods.PaymentMethods.Where(p => p.PaymentMethodId == saved.FromPaymentMethodId || p.PaymentMethodId == saved.ToPaymentMethodId).Select(p => p.OwnerPersonId);
        if (Receipts.OwnerPersonId is Guid owner && !owners.Contains(owner)) Receipts.OwnerPersonId = null;
        if (Receipts.PaymentMethodId is Guid payment && payment != saved.FromPaymentMethodId && payment != saved.ToPaymentMethodId) Receipts.PaymentMethodId = null;
        if (!Transfers.TransferEditorRules.NameMatches(saved.Name, AppliedTransferName)) AppliedTransferName = "";
        TransferText = AppliedTransferName;
    }
    public TransferListQuery TransferQuery() => new(Receipts.AllPeriods ? "all" : Receipts.AppliedMonth, Receipts.OwnerPersonId, Receipts.PaymentMethodId, AppliedTransferName);
}
