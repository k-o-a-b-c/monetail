using System.ComponentModel;
using System.Globalization;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI;

public sealed class ReceiptRow(ReceiptSummaryInfo receipt, Func<Guid, string> paymentName) : INotifyPropertyChanged
{
    public ReceiptSummaryInfo Receipt { get; } = receipt;
    public string TransactionAt => Receipt.TransactionAt.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);
    public string ShopName => Receipt.ShopName;
    public string ShopLocation => Receipt.ShopLocation ?? "";
    public string PaymentName => paymentName(Receipt.PaymentMethodId);
    public string TotalAmount => Receipt.TotalAmount.ToString("N0", CultureInfo.CurrentCulture);
    public string Memo => Receipt.Memo ?? "";
    public event PropertyChangedEventHandler? PropertyChanged;
    public void RefreshName() => PropertyChanged?.Invoke(this, new(nameof(PaymentName)));
}

public sealed record ReceiptFilterChoice(Guid? Id, string Name);
