using System.ComponentModel;
using Monetail.Windows.Client.Input;

namespace Monetail.Windows.Controls;

/// <summary>確定時に省略時刻を補完する標準TextBox。</summary>
public class TimeInput : TextBox
{
    [DefaultValue(false)]
    public bool AllowEmpty { get; set; }

    public TimeOnly? Commit()
    {
        var value = DateTimeInput.NormalizeTime(Text, AllowEmpty);
        if (!ReadOnly) Text = DateTimeInput.FormatTime(value);
        return value;
    }

    protected override void OnValidated(EventArgs e)
    {
        if (!ReadOnly) Commit();
        base.OnValidated(e);
    }
}
