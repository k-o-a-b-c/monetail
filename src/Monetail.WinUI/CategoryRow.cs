using Monetail.Windows.Client.Input;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI;

public sealed class CategoryRow(CategoryInfo category)
{
    public CategoryInfo Category { get; } = category;
    public string Name => Category.Name;
    public string IncomeExpense => IncomeLabel(Category.IncomeExpense);
    public string TaxCategory => TaxLabel(Category.TaxCategoryCode);
    public string ValidFrom => DateTimeInput.FormatDate(Category.ValidFrom);
    public string ValidTo => DateTimeInput.FormatDate(Category.ValidTo);
    public static string IncomeLabel(string code) => code switch { "Income" => "収入", "Expense" => "支出", _ => "" };
    public static string TaxLabel(string code) => code switch
    {
        "Standard" => "標準", "ReducedFood" => "食品軽減", "ReducedNewspaper" => "新聞軽減", "Exempt" => "非課税", _ => ""
    };
}
