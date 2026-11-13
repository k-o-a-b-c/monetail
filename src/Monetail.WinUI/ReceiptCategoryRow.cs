using System.ComponentModel;
using System.Globalization;
namespace Monetail.WinUI;

public sealed class ReceiptCategoryRow(Guid key, Guid categoryId, string name) : INotifyPropertyChanged
{
    public Guid Key { get; } = key;
    public Guid CategoryId { get; } = categoryId;
    public string Name { get; } = name;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string ReferenceAmount { get; private set; } = "参考額：未計算";
    internal void SetAmount(int? amount)
    {
        var text = "参考額：" + (amount?.ToString("N0", CultureInfo.CurrentCulture) ?? "未計算");
        if (ReferenceAmount == text) return;
        ReferenceAmount = text; PropertyChanged?.Invoke(this, new(nameof(ReferenceAmount)));
    }
}
