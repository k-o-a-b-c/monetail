using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Presentation.Lists;

namespace Monetail.WinUI.Views;

public sealed partial class RecordListFooter : UserControl
{
    public RecordListFooter() => InitializeComponent();

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(IRecordListState), typeof(RecordListFooter), new PropertyMetadata(null, Refresh));
    public IRecordListState? State
    {
        get => (IRecordListState?)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public static readonly DependencyProperty AutomationPrefixProperty = DependencyProperty.Register(
        nameof(AutomationPrefix), typeof(string), typeof(RecordListFooter), new PropertyMetadata("", Refresh));
    public string AutomationPrefix
    {
        get => (string)GetValue(AutomationPrefixProperty);
        set => SetValue(AutomationPrefixProperty, value);
    }

    public static readonly DependencyProperty ErrorTextProperty = DependencyProperty.Register(
        nameof(ErrorText), typeof(string), typeof(RecordListFooter), new PropertyMetadata("読み込みに失敗しました", Refresh));
    public string ErrorText
    {
        get => (string)GetValue(ErrorTextProperty);
        set => SetValue(ErrorTextProperty, value);
    }

    private static void Refresh(DependencyObject owner, DependencyPropertyChangedEventArgs args) =>
        ((RecordListFooter)owner).Bindings.Update();
    private static Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    private static string Id(string prefix, string suffix) => prefix + suffix;
}
