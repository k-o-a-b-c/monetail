namespace Monetail.Calculations.Receipts;

/// <summary>呼出元の追加検証を基本検証の元の位置で実行する。APIの型・取得処理は持ち込まない。</summary>
public interface IReceiptValidationChecks
{
    void Initialize(DateOnly transactionDate);
    void ValidateCategory(Guid categoryId, string field);
    void ValidateTaxRate(decimal? taxRate, string field);
    void ValidateExpenseType(Guid categoryId, string expenseType, string field);
}
