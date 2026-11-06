namespace Monetail.Api.Features.Monetail.Ledgers;

/// <summary>家計簿共通の科目と、将来のレシートグループへコピーする初期値。</summary>
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
    public CategoryCostKind Cost { get; set; }
    public CategoryOccurrenceKind Occurrence { get; set; }
    /// <summary>定期の場合だけ1（毎月）または12（毎年）。将来は月数を拡張できる。</summary>
    public int? RecurrenceMonths { get; set; }
    public string Revision { get; set; } = null!;
}

public enum IncomeExpenseKind { Income, Expense }
public enum CategoryCostKind { Fixed, Variable, NotApplicable }
public enum CategoryOccurrenceKind { Regular, Irregular, Extraordinary }
