namespace Monetail.Calculations.Receipts;

/// <summary>値引→税率別合算→税端数処理。参考額を会計合計へ再加算しない。</summary>
public static class ReceiptCalculator
{
    public static ReceiptAmounts Calculate(ReceiptCalculationInput input, IReceiptValidationChecks? checks = null) => Calculate(ReceiptValidation.Parse(input, checks));

    internal static ReceiptAmounts Calculate(ParsedReceipt receipt)
    {
        var all = new List<NetDetail>(); var groups = new List<ReceiptGroupAmounts>();
        for (var i = 0; i < receipt.Groups.Count; i++)
        {
            var group = receipt.Groups[i]; var nets = new List<NetDetail>(); var details = new List<ReceiptDetailAmounts>();
            for (var j = 0; j < group.Details.Count; j++)
            {
                var detail = group.Details[j]; var field = $"groups[{i}].details[{j}]";
                var net = Amount(field, () =>
                {
                    var basis = ((decimal)detail.UnitPrice - detail.UnitDiscountAmount) * detail.Quantity;
                    return basis - decimal.Ceiling(basis * detail.DiscountRate / 100m);
                });
                var item = new NetDetail(detail.TaxRate, net); nets.Add(item); all.Add(item);
                var total = Sum([item], receipt, field).Sum(row => row.TotalAmount);
                details.Add(new(net, AsInt32(total, field)));
            }
            var groupField = $"groups[{i}]";
            var groupTotal = Amount(groupField, () => Sum(nets, receipt, groupField).Sum(row => row.TotalAmount));
            groups.Add(new(group.CategoryId, AsInt32(groupTotal, groupField), details.AsReadOnly()));
        }
        var subtotals = Sum(all, receipt, "groups");
        var totalAmount = AsInt32(Amount("groups", () => subtotals.Sum(row => row.TotalAmount)), "groups");
        var income = AsInt32(Amount("groups", () => Sum(all.Where(row => row.Net < 0m), receipt, "groups").Sum(row => row.TotalAmount)), "groups");
        var expense = AsInt32(Amount("groups", () => Sum(all.Where(row => row.Net > 0m), receipt, "groups").Sum(row => row.TotalAmount)), "groups");
        return new(totalAmount, income, expense, groups.AsReadOnly(), subtotals);
    }

    private static IReadOnlyList<ReceiptTaxSubtotal> Sum(IEnumerable<NetDetail> details, ParsedReceipt receipt, string field)
    {
        var buckets = new Dictionary<TaxKey, decimal>();
        foreach (var item in details)
        {
            var key = new TaxKey(item.Rate);
            buckets[key] = Amount(field, () => buckets.GetValueOrDefault(key) + item.Net);
        }
        return buckets.Select(row => new ReceiptTaxSubtotal(row.Key.Rate, row.Value, Amount(field, () =>
        {
            if (row.Key.Rate is not decimal rate || receipt.TaxInclusionMode == 0) return row.Value;
            return receipt.TaxRoundingMode == "floor"
                ? decimal.Floor(row.Value * (1m + rate / 100m))
                : row.Value + decimal.Round(row.Value * rate / 100m, 0, MidpointRounding.AwayFromZero);
        }))).ToList().AsReadOnly();
    }

    private static decimal Amount(string field, Func<decimal> calculation)
    {
        try { return calculation(); }
        catch (OverflowException) { throw ReceiptValidation.Invalid(field, "金額の計算が扱える範囲を超えています。"); }
    }

    private static int AsInt32(decimal value, string field)
    {
        if (value < int.MinValue || value > int.MaxValue) throw ReceiptValidation.Invalid(field, "金額がInt32の範囲を超えています。");
        return decimal.ToInt32(value);
    }
    private readonly record struct TaxKey(decimal? Rate);
    private sealed record NetDetail(decimal? Rate, decimal Net);
}
