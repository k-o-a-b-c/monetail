using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Client.Input;

namespace Monetail.WinUI.Input;

/// <summary>日付入力と同じ確定境界で、Clientの時刻補完を利用する。</summary>
public sealed class TimeInputBox : TextBox
{
    public bool AllowEmpty { get; set; }
    public Func<DependencyObject?, bool>? IsCancellationTarget { get; set; }
    private bool leavingForCancellation;
    public TimeInputBox()
    {
        LosingFocus += (_, args) => leavingForCancellation = IsCancellationTarget?.Invoke(args.NewFocusedElement) == true;
        LostFocus += (_, _) => { if (!IsReadOnly && IsEnabled && !leavingForCancellation) Complete(); };
    }
    public TimeOnly? Complete()
    {
        var value = DateTimeInput.NormalizeTime(Text, AllowEmpty);
        Text = DateTimeInput.FormatTime(value); return value;
    }
}
