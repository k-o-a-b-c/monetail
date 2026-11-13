namespace Monetail.Calculations.Receipts;

// HTTP/EFモデルではない。保存APIの入力変換後に使用し、未保存値・保存済みモデルを変更しない。
public sealed record ReceiptCalculationInput(string TransactionAt, string ShopName, string? ShopLocation,
    int TaxInclusionMode, string TaxRoundingMode, long? RecurrenceInterval, string? RecurrenceUnit,
    IReadOnlyList<ReceiptCalculationGroup> Groups);
public sealed record ReceiptCalculationGroup(Guid CategoryId, IReadOnlyList<ReceiptCalculationDetail> Details);
public sealed record ReceiptCalculationDetail(string Name, long UnitPrice, long Quantity, long UnitDiscountAmount,
    string DiscountRate, string? TaxRate, string ExpenseType, string OccurrenceType, long DisplayOrder = 0);

public sealed record ReceiptAmounts(int TotalAmount, int IncomeAmount, int ExpenseAmount,
    IReadOnlyList<ReceiptGroupAmounts> Groups, IReadOnlyList<ReceiptTaxSubtotal> TaxSubtotals);
public sealed record ReceiptGroupAmounts(Guid CategoryId, int TotalAmount, IReadOnlyList<ReceiptDetailAmounts> Details);
public sealed record ReceiptDetailAmounts(decimal NetAmount, int TotalAmount);
// 税率別小計はDecimalのまま保持し、最終合計と各参考額のInt32検証とは区別する。
public sealed record ReceiptTaxSubtotal(decimal? TaxRate, decimal NetAmount, decimal TotalAmount);

/// <summary>HTTPに依存しない入力エラー。保存APIで既存invalid_inputと配列fieldへ変換する。</summary>
public sealed class ReceiptValidationException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

public sealed record ParsedReceipt(DateTime TransactionAt, int TaxInclusionMode, string TaxRoundingMode,
    IReadOnlyList<ParsedGroup> Groups);
public sealed record ParsedGroup(Guid CategoryId, IReadOnlyList<ParsedDetail> Details);
public sealed record ParsedDetail(int UnitPrice, int Quantity, int UnitDiscountAmount, decimal DiscountRate,
    decimal? TaxRate, string ExpenseType);
