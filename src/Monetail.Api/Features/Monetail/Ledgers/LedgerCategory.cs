namespace Monetail.Api.Features.Monetail.Ledgers;

/// <summary>家計簿共通の科目固有設定と、レシートグループ用の初期税区分。</summary>
public sealed class LedgerCategory
{
    public const int NameMaxLength = 100;
    private string _name = null!;
    public string LedgerId { get; set; } = null!;
    public string Id { get; set; } = null!;
    public string Name
    {
        get => _name;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var trimmed = value.Trim();
            if (trimmed.Length is < 1 or > NameMaxLength) throw new ArgumentOutOfRangeException(nameof(value));
            _name = trimmed;
        }
    }
    public bool IsEnabled { get; set; } = true;
    public int DisplayOrder { get; set; }
    public string TaxCategoryCode { get; set; } = null!;
    public IncomeExpenseKind IncomeExpense { get; set; }
    public string Revision { get; set; } = null!;
}

public enum IncomeExpenseKind { Income, Expense }
