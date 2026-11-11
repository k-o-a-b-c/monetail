using System.Globalization;
using Monetail.Windows.Client.Receipts;

namespace Monetail.WinUI;

public sealed class ReceiptDetailRow
{
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
        Discount = $"{value.UnitDiscountAmount?.ToString("N0", CultureInfo.CurrentCulture)}円 / {value.DiscountRate:0.############################}%";
        Tax = TaxLabel(value.Tax);
    }
    internal static string TaxLabel(ReceiptTaxSelection value) => !value.IsSelected ? "未選択" : value.Rate is decimal rate
        ? rate.ToString("0.############################", CultureInfo.CurrentCulture) + "%" : "非課税";
}
