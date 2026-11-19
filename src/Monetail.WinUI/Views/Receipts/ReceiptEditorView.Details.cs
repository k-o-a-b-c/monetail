using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Monetail.Windows.Client.Input;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Presentation.Receipts;
using Windows.System;
using Windows.UI.Core;

namespace Monetail.WinUI.Views.Receipts;

public sealed partial class ReceiptEditorView
{
    private bool ReadOnly => ViewModel?.IsReadOnly != false;
    public void Synchronize()
    {
        SynchronizeParent();
        SyncReceiptGroupMemo();
        SyncReceiptDetailInput();
    }
    public void RefreshMasters(bool preserveCategoryChoice)
    {
        if (rendering || ViewModel is not { } model)
            return;
        var previous = rendering;
        rendering = true;
        try
        {
            UpdatePaymentChoices(model.PaymentChoices());
            var selected = ReceiptCategoryChoice.SelectedItem as ReceiptPaymentChoice;
            var choices = model.CategoryChoices(selected, preserveCategoryChoice);
            ReceiptCategoryChoice.ItemsSource = choices;
            ReceiptCategoryChoice.SelectedItem = choices.FirstOrDefault(c => c.Id == selected?.Id);
            if (preserveCategoryChoice)
            {
                model.RefreshNames();
                RenderReceiptDetailPeople();
                RefreshReceiptTaxChoices();
            }
        }
        finally { rendering = previous; }
        UpdateReceiptCategoryActions();
        RefreshReceiptCalculation();
    }
    private void RenderReceiptGroups()
    {
        if (ViewModel is not { } model)
            return;
        var previous = rendering;
        rendering = true;
        try
        {
            ReceiptCategories.ItemsSource = model.Groups;
        }
        finally { rendering = previous; }
        SelectReceiptGroupPresentation();
    }
    private void SelectReceiptGroupPresentation()
    {
        if (ViewModel is not { } model)
            return;
        var previous = rendering;
        rendering = true;
        try
        {
            ReceiptCategories.SelectedItem = model.SelectedGroup;
            ReceiptCategoryMemo.IsEnabled = model.Editor.SelectedGroup is not null;
            ReceiptCategoryMemo.Text = model.Editor.SelectedGroup?.Memo ?? "";
        }
        finally { rendering = previous; }
        UpdateReceiptCategoryActions();
        LoadReceiptDetailForm();
    }
    private void ReceiptGroupSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (rendering)
            return;
        rendering = true;
        try
        {
            ReceiptCategories.SelectedItem = ViewModel?.SelectedGroup;
        }
        finally { rendering = false; }
    }
    private void ReceiptCategoryChoiceChanged(object sender, SelectionChangedEventArgs args) => UpdateReceiptCategoryActions();
    private void UpdateReceiptCategoryActions() => ReceiptCategoryAdd.IsEnabled = ViewModel?.CanAddCategory((ReceiptCategoryChoice.SelectedItem as ReceiptPaymentChoice)?.Id) == true;
    private async void AddReceiptCategory(object sender, RoutedEventArgs args)
    {
        if (ReceiptCategoryChoice.SelectedItem is ReceiptPaymentChoice { Id: Guid id } && ReceiptCategoryAdd.IsEnabled)
            await ChangeSelectionAsync(new(ReceiptTransitionKind.AddGroup, CategoryId: id));
    }
    private void ReceiptCategoryClicked(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is ReceiptCategoryRow row)
            ActivateReceiptCategory(row.Key);
    }
    internal async void ActivateReceiptCategory(Guid key)
    {
        if (ViewModel?.Editor.SelectedGroupKey != key)
            await ChangeSelectionAsync(new(ReceiptTransitionKind.SelectGroup, GroupKey: key));
    }
    private void ReceiptCategoryKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter || receiptEditorKeyboard.IsComposing ||
            new[] { VirtualKey.Control, VirtualKey.Menu, VirtualKey.Shift }.Any(ReceiptModifier))
            return;
        args.Handled = ActivateFocusedReceiptCategory();
    }
    internal bool ActivateFocusedReceiptCategory()
    {
        for (var element = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
            if (element is ListViewItem { Content: ReceiptCategoryRow row })
            {
                ActivateReceiptCategory(row.Key);
                return true;
            }
        return false;
    }
    private void ReceiptGroupMemoChanged(object sender, TextChangedEventArgs args) => SyncReceiptGroupMemo();
    private void SyncReceiptGroupMemo()
    {
        if (!rendering)
            ViewModel?.SetGroupMemo(ReceiptMemoValue.Normalize(ReceiptCategoryMemo.Text));
    }
    private async void RemoveReceiptCategory(object sender, RoutedEventArgs args) => await ChangeSelectionAsync(new(ReceiptTransitionKind.RemoveGroup));
    private async Task ChangeSelectionAsync(ReceiptEditTransition target)
    {
        if (ReadOnly || rendering || ViewModel is not { IsDialogOpen: false, IsTransitioning: false } model)
            return;
        Synchronize();
        var input = model.DetailInput;
        await model.TransitionCommand.ExecuteAsync(target);
        if (!ReferenceEquals(model, ViewModel))
            return;
        if (!ReferenceEquals(input, model.DetailInput))
        {
            if (!ReferenceEquals(ReceiptCategories.ItemsSource, model.Groups))
                RenderReceiptGroups();
            else if (target.Kind != ReceiptTransitionKind.SelectDetail)
                SelectReceiptGroupPresentation();
            else
                LoadReceiptDetailForm();
            var moved = target.Kind switch
            {
                ReceiptTransitionKind.RemoveGroup => model.Editor.SelectedGroupKey is null,
                ReceiptTransitionKind.AddGroup => model.Editor.SelectedGroup?.CategoryId == target.CategoryId,
                ReceiptTransitionKind.SelectGroup => model.Editor.SelectedGroupKey == target.GroupKey,
                _ => false
            };
            if (moved && target.Kind == ReceiptTransitionKind.RemoveGroup)
                ReceiptCategoryChoice.Focus(FocusState.Programmatic);
            else if (moved)
                ReceiptItemName.Focus(FocusState.Programmatic);
        }
        RefreshReceiptCalculation();
    }
    private void LoadReceiptDetailForm()
    {
        var previous = rendering;
        rendering = true;
        try
        {
            var input = ViewModel?.DetailInput;
            var value = input?.Values;
            foreach (var control in new Control[] { ReceiptItemName, ReceiptItemPrice, ReceiptItemQuantity, ReceiptItemDiscount,
                ReceiptItemRateDiscount, ReceiptItemTax, ReceiptItemExpense, ReceiptItemOccurrence, ReceiptItemMemo })
                control.IsEnabled = input is not null;
            ReceiptItemName.Text = value?.Name ?? "";
            ReceiptItemPrice.Text = input?.UnitPrice ?? "";
            ReceiptItemQuantity.Text = input?.Quantity ?? "";
            ReceiptItemDiscount.Text = input?.UnitDiscountAmount ?? "";
            ReceiptItemRateDiscount.Text = input?.DiscountRate ?? "";
            ReceiptItemMemo.Text = value?.Memo ?? "";
            RefreshReceiptTaxChoices();
            SetChoice(ReceiptItemExpense, value?.ExpenseType);
            SetChoice(ReceiptItemOccurrence, value?.OccurrenceType);
            foreach (var item in ReceiptItemExpense.Items.OfType<ComboBoxItem>())
                item.IsEnabled = ViewModel?.IsIncome != true || (string)item.Tag == "Excluded";
            RenderReceiptDetailPeople();
        }
        finally { rendering = previous; }
        RenderReceiptDetailList();
    }
    private void RenderReceiptDetailPeople()
    {
        if (ViewModel is not { DetailInput: { } input } model)
        {
            ReceiptDetailPeople.Children.Clear();
            return;
        }
        var existing = ReceiptDetailPeople.Children.OfType<CheckBox>().ToDictionary(c => (Guid)c.Tag);
        var choices = model.PeopleChoices().ToArray();
        foreach (var old in existing.Values.Where(c => choices.All(p => p.Id != (Guid)c.Tag)))
            ReceiptDetailPeople.Children.Remove(old);
        for (var i = 0; i < choices.Length; i++)
        {
            var (id, name) = choices[i];
            if (!existing.TryGetValue(id, out var box))
            {
                box = new CheckBox { Tag = id };
                AutomationProperties.SetAutomationId(box, $"ReceiptDetailPerson_{id:N}");
                box.GotFocus += (_, _) => UpdateReceiptPeopleTabStops(box);
                box.Checked += ReceiptPersonChanged;
                box.Unchecked += ReceiptPersonChanged;
            }
            box.Content = name;
            box.IsChecked = input.Values.PersonIds.Contains(id);
            box.IsEnabled = !ReadOnly;
            if (ReceiptDetailPeople.Children.IndexOf(box) != i)
            {
                ReceiptDetailPeople.Children.Remove(box);
                ReceiptDetailPeople.Children.Insert(i, box);
            }
        }
        UpdateReceiptPeopleTabStops();
    }
    private void UpdateReceiptPeopleTabStops(CheckBox? focused = null)
    {
        var boxes = ReceiptDetailPeople.Children.OfType<CheckBox>().ToArray();
        foreach (var box in boxes)
            box.IsTabStop = ReferenceEquals(box, focused ?? boxes.FirstOrDefault());
    }
    private void ReceiptPeopleKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key is not (VirtualKey.Up or VirtualKey.Down) || receiptEditorKeyboard.IsComposing)
            return;
        var boxes = ReceiptDetailPeople.Children.OfType<CheckBox>().ToArray();
        var index = Array.IndexOf(boxes, FocusManager.GetFocusedElement(XamlRoot) as CheckBox);
        if (index < 0)
            return;
        var next = Math.Clamp(index + (args.Key == VirtualKey.Up ? -1 : 1), 0, boxes.Length - 1);
        UpdateReceiptPeopleTabStops(boxes[next]);
        boxes[next].Focus(FocusState.Keyboard);
        args.Handled = true;
    }
    private void ReceiptPersonChanged(object sender, RoutedEventArgs args) => SyncReceiptDetailInput();
    private void ReceiptDetailTextChanged(object sender, TextChangedEventArgs args) => SyncReceiptDetailInput();
    private void ReceiptDetailChoiceChanged(object sender, SelectionChangedEventArgs args) => SyncReceiptDetailInput();
    private void SyncReceiptDetailInput()
    {
        if (ReadOnly || rendering || ViewModel?.DetailInput is not { } input)
            return;
        input.Values.Name = ReceiptItemName.Text;
        input.UnitPrice = ReceiptItemPrice.Text;
        input.Quantity = ReceiptItemQuantity.Text;
        input.UnitDiscountAmount = ReceiptItemDiscount.Text;
        input.DiscountRate = ReceiptItemRateDiscount.Text;
        input.Values.Tax = ReceiptItemTax.SelectedItem is ComboBoxItem { Tag: ReceiptTaxSelection tax } ? tax : ReceiptTaxSelection.Unselected;
        input.Values.ExpenseType = Choice(ReceiptItemExpense) ?? "";
        input.Values.OccurrenceType = Choice(ReceiptItemOccurrence) ?? "";
        input.Values.Memo = ReceiptMemoValue.Normalize(ReceiptItemMemo.Text);
        input.Values.PersonIds.Clear();
        input.Values.PersonIds.AddRange(ReceiptDetailPeople.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (Guid)c.Tag));
        ViewModel.SynchronizeDetail();
        RefreshReceiptCalculation();
    }
    private bool ApplyReceiptDetailCore(bool focus)
    {
        if (ViewModel is not { } model)
            return false;
        SyncReceiptDetailInput();
        if (!model.ApplyDetail())
        {
            RefreshReceiptCalculation();
            return false;
        }
        LoadReceiptDetailForm();
        if (focus)
            ReceiptItemName.Focus(FocusState.Programmatic);
        return true;
    }
    private void ApplyReceiptDetail(object sender, RoutedEventArgs args) => ApplyReceiptDetailCore(true);
    private void ReuseReceiptDetail(object sender, RoutedEventArgs args)
    {
        if (ViewModel is not { CanChangeDetail: true, IsDialogOpen: false, IsTransitioning: false } model)
            return;
        SyncReceiptDetailInput();
        model.ReuseDetail();
        RenderReceiptDetailList();
    }
    private void DeleteReceiptDetail(object sender, RoutedEventArgs args)
    {
        SyncReceiptDetailInput();
        if (ViewModel?.DeleteDetail() != true)
            return;
        LoadReceiptDetailForm();
        ReceiptItemName.Focus(FocusState.Programmatic);
    }
    private void MoveReceiptDetailUp(object sender, RoutedEventArgs args) => MoveReceiptDetail(-1);
    private void MoveReceiptDetailDown(object sender, RoutedEventArgs args) => MoveReceiptDetail(1);
    private void MoveReceiptDetail(int delta)
    {
        SyncReceiptDetailInput();
        ViewModel?.MoveDetail(delta);
        RenderReceiptDetailList();
    }
    private void RenderReceiptDetailList()
    {
        if (ViewModel is not { } model)
            return;
        var previous = rendering;
        rendering = true;
        try
        {
            if (!ReferenceEquals(ReceiptDetailsList.ItemsSource, model.Details))
                ReceiptDetailsList.ItemsSource = model.Details;
            ReceiptDetailsList.SelectedItem = model.SelectedDetail;
            model.Refresh();
        }
        finally { rendering = previous; }
        RefreshReceiptCalculation();
    }
    private async void ReceiptDetailSelected(object sender, SelectionChangedEventArgs args)
    {
        if (rendering)
            return;
        var target = ReceiptDetailsList.SelectedItem as ReceiptDetailRow;
        rendering = true;
        try
        {
            ReceiptDetailsList.SelectedItem = ViewModel?.SelectedDetail;
        }
        finally { rendering = false; }
        if (target is not null && target.Key != ViewModel?.Editor.SelectedDetailKey)
            await ChangeSelectionAsync(new(ReceiptTransitionKind.SelectDetail, DetailKey: target.Key));
    }
    private void RefreshReceiptTaxChoices()
    {
        if (ViewModel is not { TaxHistory: not null } model)
            return;
        var selected = model.DetailInput?.Values.Tax ?? ReceiptTaxSelection.Unselected;
        var previous = rendering;
        rendering = true;
        try
        {
            ReceiptItemTax.Items.Clear();
            foreach (var (tax, label) in model.TaxChoices(ConfirmedDay))
                ReceiptItemTax.Items.Add(new ComboBoxItem { Content = label, Tag = tax });
            ReceiptItemTax.SelectedItem = ReceiptItemTax.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (ReceiptTaxSelection)i.Tag == selected);
        }
        finally { rendering = previous; }
    }
    private void RefreshReceiptCalculation()
    {
        if (rendering || ViewModel is not { } model)
            return;
        var (warning, error) = model.Calculate(ConfirmedDay, IsHeaderConfirmed);
        ReceiptItemTaxWarning.Text = warning;
        ReceiptItemTaxWarning.Visibility = Show(warning.Length > 0);
        ReceiptDetailError.Text = error;
        ReceiptDetailError.Visibility = Show(error.Length > 0);
    }
    private static void SetChoice(ComboBox box, string? value) => box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == value);
    private void ReceiptDetailListKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (ViewModel?.Editor.SelectedDetailKey is not null && args.Key == VirtualKey.Enter && !receiptEditorKeyboard.IsComposing && !ReceiptModifier(VirtualKey.Control) && !ReceiptModifier(VirtualKey.Menu))
        {
            ReceiptItemName.Focus(FocusState.Keyboard);
            args.Handled = true;
        }
    }
    private void ReceiptDetailKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter || receiptEditorKeyboard.IsComposing || ReceiptItemTax.IsDropDownOpen || ReceiptItemExpense.IsDropDownOpen || ReceiptItemOccurrence.IsDropDownOpen)
            return;
        if (ReceiptModifier(VirtualKey.Control))
        {
            args.Handled = true;
            return;
        }
        if (ReceiptModifier(VirtualKey.Menu))
        {
            args.Handled = true;
            ApplyReceiptDetailCore(true);
        }
    }
    private static bool ReceiptModifier(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
    private void ReceiptDetailFormSizeChanged(object sender, SizeChangedEventArgs args)
    {
        var narrow = args.NewSize.Width < 560;
        Grid.SetRow(ReceiptDetailPeoplePane, narrow ? 1 : 0);
        Grid.SetColumn(ReceiptDetailPeoplePane, narrow ? 0 : 1);
        Grid.SetColumnSpan(ReceiptDetailPeoplePane, narrow ? 2 : 1);
        Grid.SetColumnSpan((FrameworkElement)ReceiptDetailForm.Children[0], narrow ? 2 : 1);
    }
}
