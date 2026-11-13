using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Client.Receipts;

public enum ReceiptTaxValidity { Valid, Unselected, NeedsReselection, Unknown }

/// <summary>取得済み全履歴だけを参照する。入力・履歴・保存済みの税率を変更しない。</summary>
public static class ReceiptTaxRules
{
    public static IReadOnlyList<ReceiptTaxSelection> Candidates(TaxRatesInfo history, DateOnly? day)
    {
        ArgumentNullException.ThrowIfNull(history);
        return new[] { ReceiptTaxSelection.Exempt }.Concat(day is DateOnly date
            ? history.Rates.Where(r => r.IsValidOn(date) && r.Rate is not null).Select(r => r.Rate!.Value)
                .Distinct().Order().Select(ReceiptTaxSelection.Taxed) : []).ToArray();
    }
    public static ReceiptTaxSelection Initial(TaxRatesInfo history, string categoryCode, DateOnly? day)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (categoryCode == "Exempt") return ReceiptTaxSelection.Exempt;
        var row = day is DateOnly date ? history.Rates.FirstOrDefault(r => r.TaxCategoryCode == categoryCode && r.IsValidOn(date)) : null;
        return row is null ? ReceiptTaxSelection.Unselected : row.Rate is decimal rate ? ReceiptTaxSelection.Taxed(rate) : ReceiptTaxSelection.Exempt;
    }
    public static ReceiptTaxValidity Check(TaxRatesInfo? history, DateOnly? day, ReceiptTaxSelection value)
    {
        if (!value.IsSelected) return ReceiptTaxValidity.Unselected;
        if (value.Rate is null) return ReceiptTaxValidity.Valid;
        if (history is null || day is null) return ReceiptTaxValidity.Unknown;
        return history.Rates.Any(r => r.IsValidOn(day.Value) && r.Rate == value.Rate) ? ReceiptTaxValidity.Valid : ReceiptTaxValidity.NeedsReselection;
    }
}
