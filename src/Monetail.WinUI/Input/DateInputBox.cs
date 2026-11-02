using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Monetail.Windows.Client.Input;

namespace Monetail.WinUI.Input;

/// <summary>補完は既存Clientを使い、入力中の文字列は確定まで変更しない。</summary>
public sealed class DateInputBox : TextBox
{
    public static readonly DependencyProperty AllowEmptyProperty = DependencyProperty.Register(
        nameof(AllowEmpty), typeof(bool), typeof(DateInputBox), new PropertyMetadata(true));

    public bool AllowEmpty
    {
        get => (bool)GetValue(AllowEmptyProperty);
        set => SetValue(AllowEmptyProperty, value);
    }

    public DependencyObject? CancellationTarget { get; set; }
    public Func<DependencyObject?, bool>? IsCancellationTarget { get; set; }
    public bool SuppressCompletion { get; set; }

    public DateInputBox()
    {
        LostFocus += (_, _) =>
        {
            if (SuppressCompletion || IsReadOnly || !IsEnabled || XamlRoot is null ||
                ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), CancellationTarget) ||
                IsCancellationTarget?.Invoke(FocusManager.GetFocusedElement(XamlRoot) as DependencyObject) == true) return;
            Complete();
        };
    }

    public DateOnly? Complete()
    {
        var value = DateTimeInput.NormalizeDate(Text, AllowEmpty, DateOnly.FromDateTime(DateTime.Now));
        Text = DateTimeInput.FormatDate(value);
        return value;
    }
}
