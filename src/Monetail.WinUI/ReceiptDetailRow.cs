using System.Globalization;
using System.ComponentModel;
using Monetail.Windows.Client.Receipts;

namespace Monetail.WinUI;

public sealed class ReceiptDetailRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public string ReferenceAmount { get; private set; } = "未計算";
    public string TaxWarning { get; private set; } = "";
    internal void SetCalculation(int? amount, ReceiptTaxValidity status)
    {
        var text = amount?.ToString("N0", CultureInfo.CurrentCulture) ?? "未計算";
        var warning = status switch { ReceiptTaxValidity.NeedsReselection => "要再選択", ReceiptTaxValidity.Unselected => "未選択", ReceiptTaxValidity.Unknown => "未判定", _ => "" };
        if (ReferenceAmount != text) { ReferenceAmount = text; PropertyChanged?.Invoke(this, new(nameof(ReferenceAmount))); }
        if (TaxWarning != warning) { TaxWarning = warning; PropertyChanged?.Invoke(this, new(nameof(TaxWarning))); }
    }
    public Guid Key { get; }
    public string Name { get; }
    public string Price { get; }
    public string Quantity { get; }
    public string Discount { get; }
    public string Tax { get; }
    public ReceiptDetailRow(ReceiptEditDetail detail)
    {
        Key = detail.Key; var value = detail.Values; Name = value.Name;
        Price = value.UnitPrice?.ToString("N0", CultureInfo.CurrentCulture) ?? "";
        Quantity = value.Quantity?.ToString("N0", CultureInfo.CurrentCulture) ?? "";
        var amount = value.UnitDiscountAmount is not (null or 0) ? value.UnitDiscountAmount.Value.ToString("N0", CultureInfo.CurrentCulture) + "円" : null;
        var rate = value.DiscountRate is not (null or 0) ? value.DiscountRate.Value.ToString("0.############################", CultureInfo.CurrentCulture) + "%" : null;
        Discount = amount is not null && rate is not null ? amount + " / " + rate : amount ?? rate ?? "—";
        Tax = TaxLabel(value.Tax);
    }
    internal static string TaxLabel(ReceiptTaxSelection value) => !value.IsSelected ? "未選択" : value.Rate is decimal rate
        ? rate.ToString("0.############################", CultureInfo.CurrentCulture) + "%" : "非課税";
}
