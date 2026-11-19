using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Presentation.Lists;

namespace Monetail.WinUI.Views;

public sealed partial class RecordListPanel : UserControl
{
    public RecordListPanel() => InitializeComponent();

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(IRecordListState), typeof(RecordListPanel), new PropertyMetadata(null, Refresh));
    public IRecordListState? State
    {
        get => (IRecordListState?)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public static readonly DependencyProperty HeadingProperty = DependencyProperty.Register(
        nameof(Heading), typeof(string), typeof(RecordListPanel), new PropertyMetadata("", Refresh));
    public string Heading
    {
        get => (string)GetValue(HeadingProperty);
        set => SetValue(HeadingProperty, value);
    }

    public static readonly DependencyProperty EmptyTextProperty = DependencyProperty.Register(
        nameof(EmptyText), typeof(string), typeof(RecordListPanel), new PropertyMetadata("", Refresh));
    public string EmptyText
    {
        get => (string)GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    public static readonly DependencyProperty RegisterCommandProperty = DependencyProperty.Register(
        nameof(RegisterCommand), typeof(ICommand), typeof(RecordListPanel), new PropertyMetadata(null, Refresh));
    public ICommand? RegisterCommand
    {
        get => (ICommand?)GetValue(RegisterCommandProperty);
        set => SetValue(RegisterCommandProperty, value);
    }

    public static readonly DependencyProperty ReturnToStartCommandProperty = DependencyProperty.Register(
        nameof(ReturnToStartCommand), typeof(ICommand), typeof(RecordListPanel), new PropertyMetadata(null, Refresh));
    public ICommand? ReturnToStartCommand
    {
        get => (ICommand?)GetValue(ReturnToStartCommandProperty);
        set => SetValue(ReturnToStartCommandProperty, value);
    }

    public static readonly DependencyProperty AutomationPrefixProperty = DependencyProperty.Register(
        nameof(AutomationPrefix), typeof(string), typeof(RecordListPanel), new PropertyMetadata("", Refresh));
    public string AutomationPrefix
    {
        get => (string)GetValue(AutomationPrefixProperty);
        set => SetValue(AutomationPrefixProperty, value);
    }

    public static readonly DependencyProperty RegisterAutomationIdProperty = DependencyProperty.Register(
        nameof(RegisterAutomationId), typeof(string), typeof(RecordListPanel), new PropertyMetadata("", Refresh));
    public string RegisterAutomationId
    {
        get => (string)GetValue(RegisterAutomationIdProperty);
        set => SetValue(RegisterAutomationIdProperty, value);
    }

    public static readonly DependencyProperty ListContentProperty = DependencyProperty.Register(
        nameof(ListContent), typeof(UIElement), typeof(RecordListPanel), new PropertyMetadata(null, Refresh));
    public UIElement? ListContent
    {
        get => (UIElement?)GetValue(ListContentProperty);
        set => SetValue(ListContentProperty, value);
    }

    public static readonly DependencyProperty OpeningContentProperty = DependencyProperty.Register(
        nameof(OpeningContent), typeof(UIElement), typeof(RecordListPanel), new PropertyMetadata(null, Refresh));
    public UIElement? OpeningContent
    {
        get => (UIElement?)GetValue(OpeningContentProperty);
        set => SetValue(OpeningContentProperty, value);
    }

    private static void Refresh(DependencyObject owner, DependencyPropertyChangedEventArgs args) =>
        ((RecordListPanel)owner).Bindings.Update();
    private Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    private string Id(string prefix, string suffix) => prefix + suffix;
    private string RegistrationName(string heading) => heading + "の登録";
}
