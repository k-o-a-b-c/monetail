namespace Monetail.WinUI;

[Microsoft.UI.Xaml.Data.Bindable]
public sealed class ReceiptFilterChoice(Guid? id, string name)
{
    public Guid? Id { get; } = id;
    public string Name { get; } = name;
}
