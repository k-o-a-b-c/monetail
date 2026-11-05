using Microsoft.UI.Xaml;
using Monetail.Windows.Client.Input;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI;

public sealed class PaymentRow(PaymentMethodInfo payment)
{
    public PaymentMethodInfo Payment { get; } = payment;
    public string Name => Payment.Name;
    public string ValidFrom => DateTimeInput.FormatDate(Payment.ValidFrom);
    public string ValidTo => DateTimeInput.FormatDate(Payment.ValidTo);
}

/// <summary>CollectionViewSourceの標準グループ。空のグループは画面側で除く。</summary>
public sealed class PaymentGroup(Guid ownerId, string name, bool canOrder, IEnumerable<PaymentRow> rows) : List<PaymentRow>(rows)
{
    public Guid OwnerId { get; } = ownerId;
    public string Name { get; } = name;
    public Visibility OrderVisibility { get; } = canOrder ? Visibility.Visible : Visibility.Collapsed;
}
