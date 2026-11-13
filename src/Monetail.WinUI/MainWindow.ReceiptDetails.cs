using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Receipts;
using Windows.System;
using Windows.UI.Core;

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
    private ReceiptDetailInput? receiptDetailInput;
    private bool receiptDetailRendering, receiptDetailTransition, receiptDetailValidationRequested;
    private string? receiptDetailInitialMemo;
    private string receiptDetailInitialMemoText = "";

    private void LoadReceiptDetailForm(bool refreshList = true)
    {
        receiptDetailRendering = true;
        try
        {
            receiptDetailValidationRequested = false;
            receiptDetailInput = receiptEditor?.Draft is { } draft ? new(draft) : null;
            var input = receiptDetailInput; var value = input?.Values;
            receiptDetailInitialMemo = value?.Memo;
            foreach (var control in new Control[] { ReceiptItemName, ReceiptItemPrice, ReceiptItemQuantity, ReceiptItemDiscount,
                ReceiptItemRateDiscount, ReceiptItemTax, ReceiptItemExpense, ReceiptItemOccurrence, ReceiptItemMemo }) control.IsEnabled = input is not null;
            ReceiptItemName.Text = value?.Name ?? ""; ReceiptItemPrice.Text = input?.UnitPrice ?? "";
            ReceiptItemQuantity.Text = input?.Quantity ?? ""; ReceiptItemDiscount.Text = input?.UnitDiscountAmount ?? "";
            ReceiptItemRateDiscount.Text = input?.DiscountRate ?? ""; ReceiptItemMemo.Text = value?.Memo ?? "";
            receiptDetailInitialMemoText = ReceiptItemMemo.Text;
            RefreshReceiptTaxChoices();
            SetReceiptChoice(ReceiptItemExpense, value?.ExpenseType); SetReceiptChoice(ReceiptItemOccurrence, value?.OccurrenceType);
            var income = receiptEditor?.SelectedGroup is { } group && currentLedger is { } ledger &&
                connection.Masters.Categories(ledger.LedgerId)?.Categories.FirstOrDefault(c => c.CategoryId == group.CategoryId)?.IncomeExpense == "Income";
            foreach (var item in ReceiptItemExpense.Items.OfType<ComboBoxItem>()) item.IsEnabled = !income || (string)item.Tag == "Excluded";
            RenderReceiptDetailPeople(); ReceiptDetailError.Visibility = Visibility.Collapsed;
        }
        finally { receiptDetailRendering = false; }
        RenderReceiptDetailList(refreshList);
    }
    private void RenderReceiptDetailPeople()
    {
        ReceiptDetailPeople.Children.Clear();
        if (currentLedger is null || receiptDetailInput is not { } input) return;
        var ledger = currentLedger.LedgerId; var people = connection.Masters.People(ledger)?.People ?? [];
        var mine = connection.Masters.MyPersonId(ledger);
        // 全人物を保持する。期限外の保存済み関連付けも勝手に解除しない。
        var choices = people.OrderByDescending(p => p.PersonId == mine).ThenBy(p => p.DisplayOrder).ThenBy(p => p.PersonId)
            .Select(p => (p.PersonId, p.Name)).ToList();
        foreach (var id in input.Values.PersonIds.Where(id => choices.All(p => p.PersonId != id))) choices.Add((id, "人物名未取得"));
        foreach (var (id, name) in choices)
        {
            var box = new CheckBox { Content = name, Tag = id, IsChecked = input.Values.PersonIds.Contains(id) };
            box.GotFocus += (_, _) => UpdateReceiptPeopleTabStops(box);
            box.Checked += ReceiptPersonChanged; box.Unchecked += ReceiptPersonChanged;
            ReceiptDetailPeople.Children.Add(box);
        }
        UpdateReceiptPeopleTabStops();
    }
    private void UpdateReceiptPeopleTabStops(CheckBox? focused = null)
    {
        var boxes = ReceiptDetailPeople.Children.OfType<CheckBox>().ToArray();
        foreach (var box in boxes) box.IsTabStop = ReferenceEquals(box, focused ?? boxes.FirstOrDefault());
    }
    private void ReceiptPeopleKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key is not (VirtualKey.Up or VirtualKey.Down) || receiptEditorKeyboard.IsComposing) return;
        var boxes = ReceiptDetailPeople.Children.OfType<CheckBox>().ToArray();
        var focused = FocusManager.GetFocusedElement(WorkspaceRoot.XamlRoot) as CheckBox;
        var index = Array.IndexOf(boxes, focused);
        if (index < 0) return;
        var next = Math.Clamp(index + (args.Key == VirtualKey.Up ? -1 : 1), 0, boxes.Length - 1);
        UpdateReceiptPeopleTabStops(boxes[next]); boxes[next].Focus(FocusState.Keyboard); args.Handled = true;
    }
    private void ReceiptPersonChanged(object sender, RoutedEventArgs args) => SyncReceiptDetailInput();
    private void ReceiptDetailTextChanged(object sender, TextChangedEventArgs args) => SyncReceiptDetailInput();
    private void ReceiptDetailChoiceChanged(object sender, SelectionChangedEventArgs args) => SyncReceiptDetailInput();
    private void SyncReceiptDetailInput()
    {
        if (receiptDetailRendering || receiptDetailInput is not { } input) return;
        input.Values.Name = ReceiptItemName.Text; input.UnitPrice = ReceiptItemPrice.Text; input.Quantity = ReceiptItemQuantity.Text;
        input.UnitDiscountAmount = ReceiptItemDiscount.Text; input.DiscountRate = ReceiptItemRateDiscount.Text;
        input.Values.Tax = ReceiptItemTax.SelectedItem is ComboBoxItem { Tag: ReceiptTaxSelection tax } ? tax : ReceiptTaxSelection.Unselected;
        input.Values.ExpenseType = ReceiptChoice(ReceiptItemExpense) ?? ""; input.Values.OccurrenceType = ReceiptChoice(ReceiptItemOccurrence) ?? "";
        input.Values.Memo = ReceiptOptionalText(ReceiptItemMemo.Text, receiptDetailInitialMemo, receiptDetailInitialMemoText);
        input.Values.PersonIds.Clear(); input.Values.PersonIds.AddRange(ReceiptDetailPeople.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (Guid)c.Tag));
        // 完全に解析できる間は可変下書きだけ同期。失敗時は生文字列を保護し、保存済み行には触れない。
        if (receiptEditor?.Draft is { } draft)
            try { input.ApplyTo(draft); } catch (ReceiptInputException) { }
        RefreshReceiptCalculation();

    }
    private bool ApplyReceiptDetailCore(bool focus, bool resolving = false)
    {
        if (!resolving && (receiptDetailTransition || receiptDialogOpen) || receiptEditor?.Draft is not { } draft || receiptDetailInput is null) return false;
        SyncReceiptDetailInput();
        try
        {
            receiptDetailInput.ApplyTo(draft); receiptEditor.ApplyDraft();
            LoadReceiptDetailForm(); if (focus) ReceiptItemName.Focus(FocusState.Programmatic); return true;
        }
        catch (ReceiptInputException)
        { receiptDetailValidationRequested = true; RefreshReceiptCalculation(); return false; }
    }
    private void ApplyReceiptDetail(object sender, RoutedEventArgs args) => ApplyReceiptDetailCore(true);
    private void ReuseReceiptDetail(object sender, RoutedEventArgs args)
    {
        if (receiptDialogOpen || receiptDetailTransition || receiptEditor?.SelectedDetailKey is null) return;
        SyncReceiptDetailInput(); receiptEditor.ReuseSelectedDetail(); RenderReceiptDetailList();
    }
    private void DeleteReceiptDetail(object sender, RoutedEventArgs args)
    {
        if (receiptDialogOpen || receiptDetailTransition || receiptEditor?.SelectedDetailKey is null) return;
        // 削除は明示操作。次行を選択しないので連続クリックでも他の明細は消えない。
        SyncReceiptDetailInput();
        if (receiptEditor.Draft is { } draft && receiptDetailInput is { } input)
        { draft.PersonIds.Clear(); draft.PersonIds.AddRange(input.Values.PersonIds); }
        receiptEditor.DeleteSelectedDetail(); LoadReceiptDetailForm(); ReceiptItemName.Focus(FocusState.Programmatic);
    }
    private void MoveReceiptDetailUp(object sender, RoutedEventArgs args) => MoveReceiptDetail(-1);
    private void MoveReceiptDetailDown(object sender, RoutedEventArgs args) => MoveReceiptDetail(1);
    private void MoveReceiptDetail(int delta)
    {
        if (receiptDialogOpen || receiptDetailTransition || receiptEditor is null) return;
        SyncReceiptDetailInput(); receiptEditor.MoveSelectedDetail(delta);
        RenderReceiptDetailList(); // フォームは再読込しない。不正な入力文字列もそのまま。
    }
    private void RenderReceiptDetailList(bool refreshRows = true)
    {
        receiptDetailRendering = true;
        try
        {
            // 行選択だけではコンテナーを破棄しない。矢印移動後も一覧のキーボードフォーカスを保持する。
            if (refreshRows) ReceiptDetailsList.ItemsSource = receiptEditor?.SelectedGroup?.Details.Select(d => new ReceiptDetailRow(d)).ToArray() ?? [];
            ReceiptDetailsList.SelectedItem = ReceiptDetailsList.Items.OfType<ReceiptDetailRow>().FirstOrDefault(r => r.Key == receiptEditor?.SelectedDetailKey);
            var selected = receiptEditor?.SelectedDetailKey;
            ReceiptDetailApply.IsEnabled = receiptEditor?.SelectedGroup is not null;
            ReceiptDetailApply.Content = selected is null ? "追加" : "更新";
            ReceiptDetailReuse.IsEnabled = ReceiptDetailDelete.IsEnabled = selected is not null;
            var rows = receiptEditor?.SelectedGroup?.Details;
            ReceiptDetailUp.IsEnabled = selected is not null && rows?.FirstOrDefault()?.Key != selected;
            ReceiptDetailDown.IsEnabled = selected is not null && rows?.LastOrDefault()?.Key != selected;
        }
        finally { receiptDetailRendering = false; }
        RefreshReceiptCalculation();
    }
    private async void ReceiptDetailSelected(object sender, SelectionChangedEventArgs args)
    {
        if (receiptDetailRendering) return;
        var target = ReceiptDetailsList.SelectedItem as ReceiptDetailRow;
        // WinUIの選択変更はまず元へ戻す。確認前に更新対象を変更しない。
        receiptDetailRendering = true;
        ReceiptDetailsList.SelectedItem = ReceiptDetailsList.Items.OfType<ReceiptDetailRow>().FirstOrDefault(r => r.Key == receiptEditor?.SelectedDetailKey);
        receiptDetailRendering = false;
        if (target is null || target.Key == receiptEditor?.SelectedDetailKey || receiptDetailTransition || receiptDialogOpen) return;
        receiptDetailTransition = true;
        try
        {
            if (!await ResolveReceiptDetailAsync() || receiptEditor is null) return;
            receiptEditor.SelectDetail(target.Key); LoadReceiptDetailForm(refreshList: false);
        }
        finally { receiptDetailTransition = false; }
    }
    // UIの不正な文字列も含めて保護する。解決に成功するまで選択先や元明細を変えない。
    private async Task<bool> ResolveReceiptDetailAsync()
    {
        SyncReceiptDetailInput();
        if (receiptEditor is not { } state || !(receiptDetailInput?.HasChanges == true || state.HasUnappliedChanges)) return true;
        if (receiptDialogOpen) return false;
        var dialog = ReceiptConfirmationDialog; dialog.XamlRoot = WorkspaceRoot.XamlRoot;
        dialog.Title = "未反映の明細があります"; dialog.Content = "現在の明細入力をどうしますか？";
        dialog.PrimaryButtonText = state.SelectedDetailKey is null ? "追加して移動" : "更新して移動";
        dialog.SecondaryButtonText = "入力を破棄して移動"; dialog.CloseButtonText = "編集に戻る";
        receiptDialogOpen = true;
        try
        {
            var result = await dialog.ShowAsync();
            if (!ReferenceEquals(state, receiptEditor)) return false;
            if (result == ContentDialogResult.Primary) return ApplyReceiptDetailCore(false, resolving: true);
            if (result == ContentDialogResult.Secondary) { state.DiscardDraft(); LoadReceiptDetailForm(); return true; }
            return false;
        }
        finally { receiptDialogOpen = false; dialog.SecondaryButtonText = ""; }
    }
    private void ReceiptDetailListKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (receiptEditor?.SelectedDetailKey is not null && args.Key == VirtualKey.Enter && !receiptEditorKeyboard.IsComposing && !ReceiptModifier(VirtualKey.Control) && !ReceiptModifier(VirtualKey.Menu))
        { ReceiptItemName.Focus(FocusState.Keyboard); args.Handled = true; }
    }
    private void ReceiptDetailKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter || receiptEditorKeyboard.IsComposing || ReceiptItemTax.IsDropDownOpen || ReceiptItemExpense.IsDropDownOpen || ReceiptItemOccurrence.IsDropDownOpen) return;
        if (ReceiptModifier(VirtualKey.Control)) { args.Handled = true; return; } // レシート保存は後続工程。
        if (ReceiptModifier(VirtualKey.Menu)) { args.Handled = true; ApplyReceiptDetailCore(true); }
    }
    private static bool ReceiptModifier(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
    private void ReceiptDetailFormSizeChanged(object sender, SizeChangedEventArgs args)
    {
        var narrow = args.NewSize.Width < 560;
        Grid.SetRow(ReceiptDetailPeoplePane, narrow ? 1 : 0); Grid.SetColumn(ReceiptDetailPeoplePane, narrow ? 0 : 1);
        Grid.SetColumnSpan(ReceiptDetailPeoplePane, narrow ? 2 : 1);
        var fields = (FrameworkElement)ReceiptDetailForm.Children[0]; Grid.SetColumnSpan(fields, narrow ? 2 : 1);
    }
}
