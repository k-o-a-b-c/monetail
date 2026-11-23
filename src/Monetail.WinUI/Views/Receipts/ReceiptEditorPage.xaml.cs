using System.ComponentModel;
using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Monetail.WinUI.Input;
using Monetail.Windows.Client.Input;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Presentation.Receipts;

namespace Monetail.WinUI.Views.Receipts;

/// <summary>レシート入力の実コントロール、描画、フォーカス、IMEを所有する。</summary>
public sealed partial class ReceiptEditorPage : Page
{
    public ReceiptEditorViewModel? ViewModel
    {
        get; private set;
    }
    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        ViewModel = (ReceiptEditorViewModel)args.Parameter;
        ModelChanged(null, ViewModel);
        ViewModel.ConfirmationRequested += ConfirmAsync;
        Loaded += InitialFocus;
        Populate();
    }
    private void InitialFocus(object sender, RoutedEventArgs args)
    {
        Loaded -= InitialFocus;
        ReceiptDate.Focus(FocusState.Programmatic);
    }
    protected override void OnNavigatedFrom(NavigationEventArgs args)
    {
        Release();
        base.OnNavigatedFrom(args);
    }
    private void ModelChanged(ReceiptEditorViewModel? previous, ReceiptEditorViewModel? current)
    {
        if (previous is not null)
            previous.PropertyChanged -= ModelPropertyChanged;
        if (current is not null)
            current.PropertyChanged += ModelPropertyChanged;
        Bindings.Update();
    }
    private void ModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(ReceiptEditorViewModel.DetailInput) || !ReferenceEquals(sender, ViewModel) || ViewModel is not { } model)
            return;
        var groupChanged = (ReceiptCategories.SelectedItem as ReceiptCategoryRow)?.Key != model.Editor.SelectedGroupKey;
        if (!ReferenceEquals(ReceiptCategories.ItemsSource, model.Groups))
            RenderReceiptGroups();
        else if (groupChanged)
            SelectReceiptGroupPresentation();
        else
            LoadReceiptDetailForm();
        if (groupChanged)
        {
            if (model.Editor.SelectedGroup is null)
                ReceiptCategoryChoice.Focus(FocusState.Programmatic);
            else
                ReceiptItemName.Focus(FocusState.Programmatic);
        }
    }
    public Func<DependencyObject?, bool>? IsCancellationTarget
    {
        get; set;
    }
    private bool rendering;
    private readonly FormKeyboard receiptEditorKeyboard;
    private TextBox[] TextInputs => [ReceiptDate, ReceiptTime, ReceiptShopName, ReceiptShopLocation, ReceiptInterval, ReceiptMemo, ReceiptCategoryMemo, ReceiptItemName, ReceiptItemPrice, ReceiptItemQuantity, ReceiptItemDiscount, ReceiptItemRateDiscount, ReceiptItemMemo];
    public ReceiptEditorPage()
    {
        InitializeComponent();
        receiptEditorKeyboard = new FormKeyboard(this, null, null, TextInputs);
        ReceiptDate.IsCancellationTarget = ReceiptTime.IsCancellationTarget = element =>
        {
            for (; element is not null; element = VisualTreeHelper.GetParent(element))
                if (ReferenceEquals(element, ReceiptBack) || IsCancellationTarget?.Invoke(element) == true)
                    return true;
            return false;
        };
    }
    private DateOnly? ConfirmedDay => DateOnly.TryParseExact(ReceiptDate.Text, "yyyy/MM/dd", CultureInfo.InvariantCulture,
        DateTimeStyles.None, out var day) ? day : null;
    private bool IsHeaderConfirmed => ConfirmedDay is not null && TimeOnly.TryParseExact(ReceiptTime.Text, "HH:mm:ss",
        CultureInfo.InvariantCulture, DateTimeStyles.None, out _) && ReceiptRecurrenceInput.Parse(ReceiptInterval.Text, Choice(ReceiptUnit)).IsValid;
    private bool HasUnresolvedInput => ViewModel?.HasUnresolvedInput(ReceiptDate.Text, ReceiptTime.Text, ReceiptInterval.Text, Choice(ReceiptUnit)) == true;
    public bool NeedsLeaveConfirmation => ViewModel is { } model && (model.Editor.NeedsLeaveConfirmation || model.HasUnappliedInput || HasUnresolvedInput);
    private void Populate()
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
        model.RebuildGroups();
        model.LoadDetailInput();
        model.RebuildDetails();
        RefreshMasters(false);
        RenderReceiptGroups();
        RefreshRecovery();
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
    private void SynchronizeParent()
    {
        if (rendering || ViewModel is not { IsReadOnly: false } model)
            return;
        var changed = model.UpdateHeader(ReceiptDate.Text, ReceiptTime.Text, ReceiptShopName.Text, ReceiptShopLocation.Text, ReceiptMemo.Text,
            (ReceiptEditorPayment.SelectedItem as ReceiptPaymentChoice)?.Id,
            ReceiptTaxIncluded.IsChecked == true ? 0 : ReceiptTaxExcluded.IsChecked == true ? 1 : null,
            ReceiptTaxFloor.IsChecked == true ? "floor" : ReceiptTaxHalfUp.IsChecked == true ? "half_up" : null,
            ReceiptInterval.Text, Choice(ReceiptUnit));
        if (changed)
            RefreshMasters(false);
        RefreshReceiptTaxChoices();
        RefreshReceiptCalculation();
    }
    private void UpdatePaymentChoices(ReceiptPaymentChoice[] choices)
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
    private void RefreshRecovery()
    {
        ViewModel?.Refresh();
        var readOnly = ViewModel?.IsReadOnly == true;
        foreach (var text in TextInputs)
            text.IsReadOnly = readOnly;
        foreach (var choice in new Control[] { ReceiptEditorPayment, ReceiptUnit, ReceiptTaxIncluded, ReceiptTaxExcluded, ReceiptTaxFloor, ReceiptTaxHalfUp, ReceiptCategoryChoice, ReceiptItemTax, ReceiptItemExpense, ReceiptItemOccurrence })
            choice.IsEnabled = !readOnly;
        foreach (var box in ReceiptDetailPeople.Children.OfType<CheckBox>())
            box.IsEnabled = !readOnly;
        UpdateReceiptCategoryActions();
        RenderReceiptDetailList();
    }
    private void SuppressCompletion(bool suppress) => ReceiptDate.SuppressCompletion = ReceiptTime.SuppressCompletion = suppress;
    private void Clear()
    {
        rendering = true;
        try
        {
            foreach (var input in TextInputs)
                input.Text = "";
            ReceiptEditorPayment.ItemsSource = ReceiptCategoryChoice.ItemsSource = null;
            ReceiptCategories.ItemsSource = ReceiptDetailsList.ItemsSource = null;
            ReceiptDetailPeople.Children.Clear();
            ReceiptItemTax.Items.Clear();
        }
        finally { rendering = false; }
    }
    private void RestorePosition(Control? focus, int start, int length, (ScrollViewer Viewer, double X, double Y)[] scrolls, Func<bool> current)
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
        for (var i = 0; i < ReceiptCategoryArea.Children.Count; i++)
        {
            Grid.SetRow((FrameworkElement)ReceiptCategoryArea.Children[i], narrow ? i : 0);
            Grid.SetColumn((FrameworkElement)ReceiptCategoryArea.Children[i], narrow ? 0 : i);
            Grid.SetColumnSpan((FrameworkElement)ReceiptCategoryArea.Children[i], narrow ? ReceiptCategoryArea.ColumnDefinitions.Count : 1);
        }
    }
}
