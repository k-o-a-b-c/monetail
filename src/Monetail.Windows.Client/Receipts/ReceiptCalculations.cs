using System.Globalization;
using Monetail.Calculations.Receipts;
using Monetail.Windows.Client.Ledgers;

namespace Monetail.Windows.Client.Receipts;

/// <summary>保存用/編集中の値を変更せず、APIと同じ独立計算ライブラリに渡す。通信・画面操作は行わない。</summary>
public static class ReceiptCalculations
{
    public static ReceiptAmounts Calculate(ReceiptSnapshot snapshot) => ReceiptCalculator.Calculate(FromSnapshot(snapshot));
    public static ReceiptAmounts Calculate(ReceiptEditorState editor) => ReceiptCalculator.Calculate(FromEditor(editor));

    public static ReceiptCalculationInput FromSnapshot(ReceiptSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new(Local(snapshot.TransactionAt), snapshot.ShopName, snapshot.ShopLocation, snapshot.TaxInclusionMode, snapshot.TaxRoundingMode,
            snapshot.RecurrenceInterval, snapshot.RecurrenceUnit, snapshot.Groups?.Select(g => g is null ? null! :
                new ReceiptCalculationGroup(g.CategoryId, g.Details?.Select(d => d is null ? null! :
                    new ReceiptCalculationDetail(d.Name, d.UnitPrice, d.Quantity, d.UnitDiscountAmount, Number(d.DiscountRate),
                        d.TaxRate is decimal rate ? Number(rate) : null, d.ExpenseType, d.OccurrenceType, d.DisplayOrder)).ToArray()!)).ToArray()!);
    }

    public static ReceiptCalculationInput FromEditor(ReceiptEditorState editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        if (editor.HasUnappliedChanges) throw new ReceiptInputException("detailDraft", "明細を追加/更新するか、入力を明示的に破棄してください。");
        var header = editor.Header;
        return new(Local(Required(header.TransactionAt, "transactionAt")), header.ShopName, header.ShopLocation,
            Required(header.TaxInclusionMode, "taxInclusionMode"), header.TaxRoundingMode!, header.RecurrenceInterval, header.RecurrenceUnit,
            editor.Groups.Select((group, i) => new ReceiptCalculationGroup(group.CategoryId, group.Details.Select((row, j) =>
            {
                var value = row.Values; var path = $"groups[{i}].details[{j}]";
                if (!value.Tax.IsSelected) throw new ReceiptInputException(path + ".taxRate", "税率を選択してください。");
                return new ReceiptCalculationDetail(value.Name, Required(value.UnitPrice, path + ".unitPrice"), Required(value.Quantity, path + ".quantity"),
                    Required(value.UnitDiscountAmount, path + ".unitDiscountAmount"), Number(Required(value.DiscountRate, path + ".discountRate")),
                    value.Tax.Rate is decimal rate ? Number(rate) : null, value.ExpenseType, value.OccurrenceType, group.Order(j, group.IsOriginalOrder));
            }).ToArray())).ToArray());
    }
    private static T Required<T>(T? value, string field) where T : struct => value ?? throw new ReceiptInputException(field, "入力してください。");
    private static string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    // 小数秒を切り捨てずに渡し、共通検証で拒否する。Kindによるタイムゾーン変換もしない。
    private static string Local(DateTime value) => value.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);
}
