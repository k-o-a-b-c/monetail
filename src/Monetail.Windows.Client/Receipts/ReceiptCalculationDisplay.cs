using Monetail.Windows.Client.Ledgers;
using Monetail.Calculations.Receipts;
using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Client.Receipts;

/// <summary>全件が計算可能な時だけ正式額を公開する。下書き・選択・ID・更新版は変更しない。</summary>
public sealed record ReceiptCalculationDisplay(ReceiptAmounts? Amounts, string? Field, string? Message,
    IReadOnlyDictionary<Guid, ReceiptTaxValidity> Taxes, IReadOnlyDictionary<Guid, int> Groups, IReadOnlyDictionary<Guid, int> Details)
{
    public static ReceiptCalculationDisplay Evaluate(ReceiptEditorState editor, TaxRatesInfo? history, DateOnly? day, bool headerConfirmed)
    {
        var taxes = editor.Groups.SelectMany(g => g.Details).ToDictionary(d => d.Key, d => ReceiptTaxRules.Check(history, day, d.Values.Tax));
        ReceiptCalculationDisplay Failed(string? field, string message) => new(null, field, message, taxes, new Dictionary<Guid, int>(), new Dictionary<Guid, int>());
        if (history is null) return Failed(null, "税率履歴を取得できていません。");
        if (day is null || !headerConfirmed) return Failed("transactionAt", "日時・周期の入力を確定してください。");
        if (taxes.Values.Any(v => v != ReceiptTaxValidity.Valid)) return Failed("groups", "未選択または取引日に適用できない税率があります。税率を再選択してください。");
        try
        {
            var result = ReceiptCalculations.CalculateApplied(editor);
            var groups = new Dictionary<Guid, int>(); var details = new Dictionary<Guid, int>();
            for (var i = 0; i < editor.Groups.Count; i++)
            {
                var group = editor.Groups[i]; groups[group.Key] = result.Groups[i].TotalAmount;
                for (var j = 0; j < group.Details.Count; j++) details[group.Details[j].Key] = result.Groups[i].Details[j].TotalAmount;
            }
            return new(result, null, null, taxes, groups, details);
        }
        catch (ReceiptValidationException error) { return Failed(error.Field, error.Message); }
        catch (ReceiptInputException error) { return Failed(error.Field, error.Message); }
    }
}
