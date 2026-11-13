using Monetail.Api.Features.Monetail.Ledgers;
using Shared = Monetail.Calculations.Receipts;

namespace Monetail.Api.Features.Monetail.Receipts;

/// <summary>取得済みマスタの検証だけをAPIに残し、元の判定順序で共通計算へ接続する。</summary>
public static class ReceiptValidation
{
    public static ReceiptAmounts ValidateForSave(ReceiptCalculationInput input, ReceiptMasterData masters)
    {
        ArgumentNullException.ThrowIfNull(masters);
        return Shared.ReceiptCalculator.Calculate(input, new MasterChecks(masters));
    }
    internal static Shared.ParsedReceipt Parse(ReceiptCalculationInput input) => Shared.ReceiptValidation.Parse(input);
    internal static DateTime ParseHeader(ReceiptCalculationInput input) => Shared.ReceiptValidation.ParseHeader(input);
    internal static decimal DecimalValue(string? value, string field) => Shared.ReceiptValidation.DecimalValue(value, field);
    internal static ReceiptValidationException Invalid(string field, string message) => Shared.ReceiptValidation.Invalid(field, message);

    private sealed class MasterChecks(ReceiptMasterData masters) : Shared.IReceiptValidationChecks
    {
        private HashSet<decimal> rates = null!;
        private IncomeExpenseKind? kind;
        public void Initialize(DateOnly day) => rates = masters.TaxPeriods
            .Where(period => period.ValidFrom <= day && (period.ValidTo is null || day <= period.ValidTo))
            .Where(period => period.Rate is not null).Select(period => period.Rate!.Value).ToHashSet();
        public void ValidateCategory(Guid categoryId, string field)
        {
            if (!masters.CategoryIncomeExpenses.TryGetValue(categoryId, out var found) || !Enum.IsDefined(found))
                throw Invalid(field, "科目を確認してください。");
            kind = found;
        }
        public void ValidateTaxRate(decimal? taxRate, string field)
        {
            // 非課税は常に別候補。課税0%は取引日に適用される履歴が必要。
            if (taxRate is decimal rate && !rates.Contains(rate))
                throw Invalid(field, "取引日に適用される税率を選択してください。");
        }
        public void ValidateExpenseType(Guid categoryId, string expenseType, string field)
        {
            if (kind == IncomeExpenseKind.Income && expenseType != "Excluded")
                throw Invalid(field, "収入科目の費用区分は対象外を指定してください。");
        }
    }
}
