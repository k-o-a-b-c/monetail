using Monetail.Windows.Client.Input;
using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Presentation.Management;
public sealed record PersonRow(PersonInfo Person, bool IsMe)
{
    public string Name => Person.Name + (IsMe ? "（本人）" : "");
    public string ValidFrom => DateTimeInput.FormatDate(Person.ValidFrom);
    public string ValidTo => DateTimeInput.FormatDate(Person.ValidTo);
}
