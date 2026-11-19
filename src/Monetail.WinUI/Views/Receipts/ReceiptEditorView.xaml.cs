using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Monetail.WinUI.Input;
using Monetail.Windows.Client.Input;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Presentation.Receipts;

namespace Monetail.WinUI.Views.Receipts;

/// <summary>親情報と画面枠。科目/明細のContentとその状態・イベントは第1段階ではWindowに残す。</summary>
public sealed partial class ReceiptEditorView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(nameof(ViewModel),
        typeof(ReceiptEditorViewModel), typeof(ReceiptEditorView), new PropertyMetadata(null,
            (owner, _) => ((ReceiptEditorView)owner).Bindings.Update()));
    public ReceiptEditorViewModel? ViewModel
    {
        get => (ReceiptEditorViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }
    public static readonly DependencyProperty DetailsContentProperty = DependencyProperty.Register(nameof(DetailsContent),
        typeof(UIElement), typeof(ReceiptEditorView), new PropertyMetadata(null));
    public UIElement? DetailsContent
    {
        get => (UIElement?)GetValue(DetailsContentProperty);
        set => SetValue(DetailsContentProperty, value);
    }
    public Func<DependencyObject?, bool>? IsCancellationTarget
    {
        get; set;
    }
    public event Action<bool>? ParentChanged;
    public event Action<bool>? CompactLayoutChanged;
    private bool rendering;
    private readonly FormKeyboard receiptEditorKeyboard;
    public bool IsComposing => receiptEditorKeyboard.IsComposing;
    private TextBox[] ParentTextInputs => [ReceiptDate, ReceiptTime, ReceiptShopName, ReceiptShopLocation, ReceiptInterval, ReceiptMemo];
    public ReceiptEditorView()
    {
        InitializeComponent();
        receiptEditorKeyboard = new FormKeyboard(this, null, null, ParentTextInputs);
        ReceiptDate.IsCancellationTarget = ReceiptTime.IsCancellationTarget = element =>
        {
            for (; element is not null; element = VisualTreeHelper.GetParent(element))
                if (ReferenceEquals(element, ReceiptBack) || IsCancellationTarget?.Invoke(element) == true)
                    return true;
            return false;
        };
    }
    // 同じ画面内の科目/明細も、一つのIME/Enterスコープとして既存どおり監視する。
    internal void ObserveDetailInputs(params TextBox[] inputs)
    {
        foreach (var input in inputs)
            receiptEditorKeyboard.ObserveInput(input);
    }
    public DateOnly? ConfirmedDay => DateOnly.TryParseExact(ReceiptDate.Text, "yyyy/MM/dd", CultureInfo.InvariantCulture,
        DateTimeStyles.None, out var day) ? day : null;
    public bool IsHeaderConfirmed => ConfirmedDay is not null && TimeOnly.TryParseExact(ReceiptTime.Text, "HH:mm:ss",
        CultureInfo.InvariantCulture, DateTimeStyles.None, out _) && ReceiptRecurrenceInput.Parse(ReceiptInterval.Text, Choice(ReceiptUnit)).IsValid;
    public bool HasUnresolvedInput => ViewModel?.HasUnresolvedInput(ReceiptDate.Text, ReceiptTime.Text, ReceiptInterval.Text, Choice(ReceiptUnit)) == true;
    public void Populate()
    {
        if (ViewModel is not { } model)
            return;
        rendering = true;
        try
        {
            var header = model.Editor.Header;
            ReceiptDate.Text = DateTimeInput.FormatDate(header.TransactionAt is DateTime at ? DateOnly.FromDateTime(at) : null);
            ReceiptTime.Text = DateTimeInput.FormatTime(header.TransactionAt is DateTime time ? TimeOnly.FromDateTime(time) : null);
            ReceiptShopName.Text = header.ShopName;
            ReceiptShopLocation.Text = header.ShopLocation ?? "";
            ReceiptMemo.Text = header.Memo ?? "";
            RenderTaxValues();
            ReceiptInterval.Text = header.RecurrenceInterval?.ToString(CultureInfo.InvariantCulture) ?? "";
            ReceiptUnit.SelectedItem = ReceiptUnit.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == (header.RecurrenceUnit ?? "Month"));
            model.Refresh();
        }
        finally { rendering = false; }
    }
    internal void RenderTaxValues()
    {
        if (ViewModel is not { } model)
            return;
        var previous = rendering;
        rendering = true;
        try
        {
            ReceiptTaxIncluded.IsChecked = model.Editor.Header.TaxInclusionMode == 0;
            ReceiptTaxExcluded.IsChecked = model.Editor.Header.TaxInclusionMode == 1;
            ReceiptTaxFloor.IsChecked = model.Editor.Header.TaxRoundingMode == "floor";
            ReceiptTaxHalfUp.IsChecked = model.Editor.Header.TaxRoundingMode == "half_up";
        }
        finally { rendering = previous; }
    }
    public void SynchronizeParent()
    {
        if (rendering || ViewModel is not { IsReadOnly: false } model)
            return;
        var changed = model.UpdateHeader(ReceiptDate.Text, ReceiptTime.Text, ReceiptShopName.Text, ReceiptShopLocation.Text, ReceiptMemo.Text,
            (ReceiptEditorPayment.SelectedItem as ReceiptPaymentChoice)?.Id,
            ReceiptTaxIncluded.IsChecked == true ? 0 : ReceiptTaxExcluded.IsChecked == true ? 1 : null,
            ReceiptTaxFloor.IsChecked == true ? "floor" : ReceiptTaxHalfUp.IsChecked == true ? "half_up" : null,
            ReceiptInterval.Text, Choice(ReceiptUnit));
        ParentChanged?.Invoke(changed);
    }
    public void UpdatePaymentChoices(ReceiptPaymentChoice[] choices)
    {
        var previous = rendering;
        rendering = true;
        try
        {
            ReceiptEditorPayment.ItemsSource = choices;
            ReceiptEditorPayment.SelectedItem = choices.FirstOrDefault(c => c.Id == ViewModel?.Editor.Header.PaymentMethodId);
        }
        finally { rendering = previous; }
    }
    public void RefreshRecovery()
    {
        ViewModel?.Refresh();
        var readOnly = ViewModel?.IsReadOnly == true;
        foreach (var text in ParentTextInputs)
            text.IsReadOnly = readOnly;
        foreach (var choice in new Control[] { ReceiptEditorPayment, ReceiptUnit, ReceiptTaxIncluded, ReceiptTaxExcluded, ReceiptTaxFloor, ReceiptTaxHalfUp })
            choice.IsEnabled = !readOnly;
    }
    public void SuppressCompletion(bool suppress) => ReceiptDate.SuppressCompletion = ReceiptTime.SuppressCompletion = suppress;
    public void ClearParent()
    {
        rendering = true;
        try
        {
            foreach (var input in ParentTextInputs)
                input.Text = "";
            ReceiptEditorPayment.ItemsSource = null;
        }
        finally { rendering = false; }
    }
    public void RestorePosition(Control? focus, int start, int length, (ScrollViewer Viewer, double X, double Y)[] scrolls, Func<bool> current)
    {
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (!current())
                return;
            ReceiptInputScroller.UpdateLayout();
            if (focus is { IsLoaded: true, IsEnabled: true, Visibility: Visibility.Visible })
            {
                focus.Focus(FocusState.Programmatic);
                if (focus is TextBox text)
                    text.Select(start, length);
            }
            foreach (var (viewer, x, y) in scrolls)
                if (viewer.IsLoaded)
                    viewer.ChangeView(x, y, null, true);
            SuppressCompletion(false);
        });
    }
    private static string? Choice(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
    private Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    private Visibility HasText(string? text) => Show(!string.IsNullOrEmpty(text));
    private void ReceiptParentTextChanged(object sender, TextChangedEventArgs args) => SynchronizeParent();
    private void ReceiptParentChoiceChanged(object sender, SelectionChangedEventArgs args) => SynchronizeParent();
    private void ReceiptTaxChecked(object sender, RoutedEventArgs args) => SynchronizeParent();
    private void ReceiptTaxSelected(object sender, RoutedEventArgs args)
    {
        if (rendering || ViewModel is not { IsReadOnly: false } model || sender is not RadioButton radio)
            return;
        if (radio.GroupName == "ReceiptInclusion")
            model.ParentInput.SelectTaxInclusion(int.Parse((string)radio.Tag, CultureInfo.InvariantCulture));
        else
            model.ParentInput.SelectTaxRounding((string)radio.Tag);
        SynchronizeParent();
    }
    private void ParentSizeChanged(object sender, SizeChangedEventArgs args)
    {
        var narrow = args.NewSize.Width < 780;
        foreach (var grid in new[] { ReceiptHeaderRow1, ReceiptHeaderRow2, ReceiptHeaderRow3, ReceiptHeaderRow4 })
            for (var i = 0; i < grid.Children.Count; i++)
            {
                Grid.SetRow((FrameworkElement)grid.Children[i], narrow ? i : 0);
                Grid.SetColumn((FrameworkElement)grid.Children[i], narrow ? 0 : i);
                Grid.SetColumnSpan((FrameworkElement)grid.Children[i], narrow ? grid.ColumnDefinitions.Count : 1);
            }
        CompactLayoutChanged?.Invoke(narrow);
    }
}
