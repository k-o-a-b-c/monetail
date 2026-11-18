using CommunityToolkit.Mvvm.ComponentModel;
using System.Globalization;
using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Presentation.Receipts;

public sealed class ReceiptListItem(ReceiptSummaryInfo receipt, Func<Guid, string> paymentName) : ObservableObject
{
    public ReceiptSummaryInfo Receipt { get; } = receipt;
    public Guid ReceiptId => Receipt.ReceiptId;
    public string TransactionAt => Receipt.TransactionAt.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);
    public string ShopName => Receipt.ShopName;
    public string ShopLocation => Receipt.ShopLocation ?? "";
    public string PaymentName => paymentName(Receipt.PaymentMethodId);
    public string TotalAmount => Receipt.TotalAmount.ToString("N0", CultureInfo.CurrentCulture);
    public string Memo => Receipt.Memo ?? "";
    public void RefreshName() => OnPropertyChanged(nameof(PaymentName));
}
