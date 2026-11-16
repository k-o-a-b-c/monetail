using System.ComponentModel;
using System.Globalization;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI;

[Microsoft.UI.Xaml.Data.Bindable]
public sealed class TransferRow(TransferSummaryInfo transfer, Func<Guid, string> paymentName) : INotifyPropertyChanged
{
    public TransferSummaryInfo Transfer { get; } = transfer;
    public string TransactionAt => Transfer.TransactionAt.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);
    public string Name => Transfer.Name;
    public string FromName => paymentName(Transfer.FromPaymentMethodId);
    public string ToName => paymentName(Transfer.ToPaymentMethodId);
    public string Amount => Transfer.Amount.ToString("N0", CultureInfo.CurrentCulture);
    public string Memo => Transfer.Memo ?? "";
    public event PropertyChangedEventHandler? PropertyChanged;
    public void RefreshName() { PropertyChanged?.Invoke(this, new(nameof(FromName))); PropertyChanged?.Invoke(this, new(nameof(ToName))); }
}
