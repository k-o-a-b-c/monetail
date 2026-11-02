using System.ComponentModel;
using Monetail.Windows.Client.Input;

namespace Monetail.Windows.Controls;

/// <summary>確定時に省略日付を補完する標準TextBox。</summary>
public class DateInput : TextBox
{
    [DefaultValue(false)]
    public bool AllowEmpty { get; set; }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TimeProvider TimeProvider { get; set; } = System.TimeProvider.System;

    public DateOnly? Commit()
    {
        var today = DateOnly.FromDateTime(TimeProvider.GetLocalNow().DateTime);
        var value = DateTimeInput.NormalizeDate(Text, AllowEmpty, today);
        if (!ReadOnly) Text = DateTimeInput.FormatDate(value);
        return value;
    }

    protected override void OnValidated(EventArgs e)
    {
        if (!ReadOnly) Commit();
        base.OnValidated(e);
    }
}
