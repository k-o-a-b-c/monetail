using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Presentation.Transfers;

/// <summary>保存済みの一覧行を投影し、共有マスタの名称だけを再表示する。</summary>
public sealed class TransferListItem(TransferSummaryInfo transfer, Func<Guid, string> paymentName) : ObservableObject
{
    public TransferSummaryInfo Transfer { get; } = transfer;
    public Guid TransferId => Transfer.TransferId;
    public string TransactionAt => Transfer.TransactionAt.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);
    public string Name => Transfer.Name;
    public string FromName => paymentName(Transfer.FromPaymentMethodId);
    public string ToName => paymentName(Transfer.ToPaymentMethodId);
    public string Amount => Transfer.Amount.ToString("N0", CultureInfo.CurrentCulture);
    public string Memo => Transfer.Memo ?? "";

    public void RefreshName()
    {
        OnPropertyChanged(nameof(FromName));
        OnPropertyChanged(nameof(ToName));
    }
}
