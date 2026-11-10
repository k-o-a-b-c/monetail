using System.Globalization;
using Monetail.Api.Features.Monetail.Ledgers;

namespace Monetail.Api.Features.Monetail.Receipts;

/// <summary>単独入力検証と、呼出元で取得したマスタの照合を分離する。DB・認証・保存処理は持たない。</summary>
public static class ReceiptValidation
{
    public static ReceiptAmounts ValidateForSave(ReceiptCalculationInput input, ReceiptMasterData masters)
    {
        ArgumentNullException.ThrowIfNull(masters);
        return ReceiptCalculator.Calculate(Parse(input, masters));
    }

    internal static ParsedReceipt Parse(ReceiptCalculationInput input, ReceiptMasterData? masters = null)
    {
        var at = ParseHeader(input);
        var day = DateOnly.FromDateTime(at);
        var rates = masters?.TaxPeriods.Where(period => period.ValidFrom <= day && (period.ValidTo is null || day <= period.ValidTo))
            .Where(period => period.Rate is not null).Select(period => period.Rate!.Value).ToHashSet();
        var groups = new List<ParsedGroup>(); var categories = new HashSet<Guid>(); var regular = false;
        for (var i = 0; i < input.Groups.Count; i++)
        {
            var group = input.Groups[i]; var field = $"groups[{i}]";
            if (group is null) throw Invalid(field, "科目を指定してください。");
            if (group.CategoryId == Guid.Empty || !categories.Add(group.CategoryId))
                throw Invalid(field + ".categoryId", "科目を重複なく指定してください。");
            IncomeExpenseKind? kind = null;
            if (masters is not null)
            {
                if (!masters.CategoryIncomeExpenses.TryGetValue(group.CategoryId, out var found) || !Enum.IsDefined(found))
                    throw Invalid(field + ".categoryId", "科目を確認してください。");
                kind = found;
            }
            if (group.Details is null || group.Details.Count == 0) throw Invalid(field + ".details", "明細を1件以上指定してください。");
            var details = new List<ParsedDetail>();
            for (var j = 0; j < group.Details.Count; j++)
            {
                var detail = group.Details[j]; var path = $"{field}.details[{j}]";
                if (detail is null) throw Invalid(path, "明細を指定してください。");
                Int32(detail.DisplayOrder, path + ".displayOrder", nonNegative: true);
                Text(detail.Name, path + ".name", required: true);
                var price = Int32(detail.UnitPrice, path + ".unitPrice");
                var quantity = Int32(detail.Quantity, path + ".quantity");
                if (quantity < 1) throw Invalid(path + ".quantity", "数量は1以上を指定してください。");
                var discountAmount = Int32(detail.UnitDiscountAmount, path + ".unitDiscountAmount");
                var discountRate = DecimalValue(detail.DiscountRate, path + ".discountRate");
                if (discountRate is < 0m or > 100m) throw Invalid(path + ".discountRate", "値引率は0〜100を指定してください。");
                var taxRate = detail.TaxRate is null ? (decimal?)null : DecimalValue(detail.TaxRate, path + ".taxRate");
                // マスタは呼出元が同一家計簿・保存トランザクション内で取得する。ここではI/Oを行わない。
                // 非課税は常に別候補。課税0%は取引日に適用される履歴が必要。
                if (rates is not null && taxRate is decimal rate && !rates.Contains(rate))
                    throw Invalid(path + ".taxRate", "取引日に適用される税率を選択してください。");
                if (detail.ExpenseType is not ("Fixed" or "Variable" or "Excluded")) throw Invalid(path + ".expenseType", "費用区分を指定してください。");
                if (kind == IncomeExpenseKind.Income && detail.ExpenseType != "Excluded")
                    throw Invalid(path + ".expenseType", "収入科目の費用区分は対象外を指定してください。");
                if (detail.OccurrenceType is not ("Regular" or "Irregular" or "Temporary")) throw Invalid(path + ".occurrenceType", "発生区分を指定してください。");
                regular |= detail.OccurrenceType == "Regular";
                details.Add(new(price, quantity, discountAmount, discountRate, taxRate, detail.ExpenseType));
            }
            groups.Add(new(group.CategoryId, details.AsReadOnly()));
        }
        if (input.RecurrenceInterval is not null && !regular) throw Invalid("groups", "周期を設定する場合は定期の明細が必要です。");
        return new(at, input.TaxInclusionMode, input.TaxRoundingMode, groups.AsReadOnly());
    }

    internal static DateTime ParseHeader(ReceiptCalculationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.TransactionAt is null || input.TransactionAt.Length != 19 ||
            !DateTime.TryParseExact(input.TransactionAt, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var at))
            throw Invalid("transactionAt", "有効なローカル日時を秒まで指定してください。");
        Text(input.ShopName, "shopName", required: true, nonEmpty: true);
        Text(input.ShopLocation, "shopLocation", required: false);
        if (input.TaxInclusionMode is not (0 or 1)) throw Invalid("taxInclusionMode", "税込または税抜を指定してください。");
        if (input.TaxRoundingMode is not ("floor" or "half_up")) throw Invalid("taxRoundingMode", "税端数方式を指定してください。");
        if (input.RecurrenceInterval is long interval && (interval < 1 || interval > int.MaxValue))
            throw Invalid("recurrenceInterval", "周期の間隔は1以上のInt32整数を指定してください。");
        if (input.RecurrenceInterval is null && input.RecurrenceUnit is not null || input.RecurrenceInterval is not null && input.RecurrenceUnit is null)
            throw Invalid("recurrenceUnit", "周期の間隔と単位を両方指定するか、両方未設定にしてください。");
        if (input.RecurrenceUnit is not (null or "Week" or "Month" or "Year"))
            throw Invalid("recurrenceUnit", "週・月・年のいずれかを指定してください。");
        if (input.Groups is null || input.Groups.Count == 0) throw Invalid("groups", "科目を1件以上指定してください。");
        return at;
    }

    private static void Text(string? value, string field, bool required, bool nonEmpty = false)
    {
        if (required && value is null || nonEmpty && value?.Length == 0 || value?.Length > 100)
            throw Invalid(field, "文字列は100 UTF-16コード単位以内で指定してください。");
        // Trim・空白除去・メモ上限等、未承認の書換えや入力条件は追加しない。
    }

    private static int Int32(long value, string field, bool nonNegative = false)
    {
        if (value < (nonNegative ? 0 : int.MinValue) || value > int.MaxValue)
            throw Invalid(field, "Int32の範囲内の整数を指定してください。");
        return (int)value;
    }

    internal static decimal DecimalValue(string? value, string field)
    {
        const NumberStyles style = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
        if (value is null || !decimal.TryParse(value, style, CultureInfo.InvariantCulture, out var parsed) ||
            Canonical(value) != Canonical(parsed.ToString("0.############################", CultureInfo.InvariantCulture)))
            throw Invalid(field, "正確にDecimalで表現できる数値文字列を指定してください。");
        return parsed;
    }

    private static string Canonical(string value)
    {
        var negative = value.StartsWith('-'); var parts = value.TrimStart('+', '-').Split('.');
        var whole = parts[0].TrimStart('0'); if (whole.Length == 0) whole = "0";
        var fraction = parts.Length == 2 ? parts[1].TrimEnd('0') : "";
        return (negative && (whole != "0" || fraction.Length > 0) ? "-" : "") + whole + (fraction.Length > 0 ? "." + fraction : "");
    }

    internal static ReceiptValidationException Invalid(string field, string message) => new(field, message);
}
