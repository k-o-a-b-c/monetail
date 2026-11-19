using System.Net;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Input;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Presentation.Receipts;
using Windows.System;
using Windows.UI.Core;

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
    private ReceiptEditorState? receiptEditor;
    private TaxRatesInfo? receiptTaxRates;
    private Guid? receiptOpenRetryId;
    private CancellationTokenSource? receiptOpening;
    private bool receiptEditorRendering, receiptDialogOpen, receiptLeaving, receiptCloseApproved, receiptRestorePending;

    private void InitializeReceiptEditor()
    {
        WorkspaceRoot.Children.Remove(ReceiptConfirmationDialog);
        ReceiptEntry.ObserveDetailInputs(ReceiptCategoryMemo, ReceiptItemName, ReceiptItemPrice, ReceiptItemQuantity,
            ReceiptItemDiscount, ReceiptItemRateDiscount, ReceiptItemMemo);
        ReceiptEntry.IsCancellationTarget = IsReceiptLeaveTarget;
        ReceiptEntry.ParentChanged += changed =>
        {
            if (changed) RefreshReceiptEditorChoices();
            RefreshReceiptTaxChoices();
            RefreshReceiptCalculation();
        };
        ReceiptEntry.CompactLayoutChanged += ResizeReceiptDetails;
        AppWindow.Closing += async (_, args) =>
        {
            if (receiptRecoveryPhase == ReceiptRecoveryPhase.Login) { DiscardReceiptAuthentication(); return; }
            if (receiptCloseApproved || receiptEditor is null) return;
            args.Cancel = true;
            if (await LeaveReceiptEditorAsync()) { receiptCloseApproved = true; Close(); }
        };
    }
    private bool IsReceiptLeaveTarget(DependencyObject? element)
    {
        for (; element is not null; element = VisualTreeHelper.GetParent(element))
            if (ReferenceEquals(element, SettingsButton) ||
                ReferenceEquals(element, RecordButton) || ReferenceEquals(element, LedgerSelector)) return true;
        return false;
    }
    // 一要求だけを受理。取消後に通信が完了しても同一要求・本人・家計簿・画面を照合して適用しない。
    internal async Task OpenReceiptEditorAsync(Guid? receiptId)
    {
        if (busy || closed || ManagementDialogOpen || receiptOpening is not null || transferOpening is not null || RecordEditorActive || account is null || currentLedger is null || HomePage.Visibility != Visibility.Visible) return;
        var ledger = currentLedger.LedgerId; var person = account.AuthUserId;
        var api = connection.Api; var session = connection.Session;
        var initial = new ReceiptHeaderDraft { TransactionAt = DateTime.Today, ShopLocation = "", Memo = null, TaxInclusionMode = 0, TaxRoundingMode = "floor" };
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        receiptOpening = pending;
        receiptOpenRetryId = receiptId;
        ReceiptOpenRetry.Visibility = Visibility.Collapsed;
        UpdateReceiptActions();
        ReceiptOpenNotice.Visibility = Visibility.Visible; ReceiptOpenProgress.IsActive = true; ReceiptOpenMessage.Text = "レシートを読み込み中…";
        bool Current() => !closed && ReferenceEquals(receiptOpening, pending) && !pending.IsCancellationRequested &&
            currentLedger?.LedgerId == ledger && account?.AuthUserId == person && HomePage.Visibility == Visibility.Visible;
        try
        {
            var info = receiptId is Guid id ? await new ReceiptsWorkspace(api, session, ledger).GetAsync(id, pending.Token) : null;
            await connection.Masters.GetPeopleAsync(ledger, ct => new PeopleWorkspace(api, session, ledger).ListAsync(ct), pending.Token);
            await connection.Masters.GetPaymentsAsync(ledger, ct => new PaymentMethodsWorkspace(api, session, ledger).ListAsync(ct), pending.Token);
            await connection.Masters.GetCategoriesAsync(ledger, ct => new CategoriesWorkspace(api, session, ledger).ListAsync(ct), pending.Token);
            if (!Current()) return;
            var taxes = await new TaxRatesWorkspace(api, session, ledger).ListAsync(pending.Token);
            if (!Current()) return;
            receiptTaxRates = taxes;
            ReceiptCategoryDefaults Defaults(Guid category, DateTime? date)
            {
                var master = connection.Masters.Categories(ledger)?.Categories.FirstOrDefault(c => c.CategoryId == category)
                    ?? throw new ReceiptInputException("categoryId", "科目情報を取得できませんでした。");
                // 未確定の生文字列がある間は、最後の有効なモデル日付で初期値を代用しない。
                return new(master.IncomeExpense, receiptTaxRates is { } currentTaxes ? ReceiptTaxRules.Initial(currentTaxes, master.TaxCategoryCode, ConfirmedReceiptDay) : ReceiptTaxSelection.Unselected);
            }
            receiptEditor = info is null ? ReceiptEditorState.Create(initial, Defaults) : ReceiptEditorState.Edit(info, Defaults);
            var model = new ReceiptEditorViewModel(receiptEditor, () => new(ReceiptRecoveryReadOnly,
                ReceiptRecoveryMessage, receiptRecoveryPhase == ReceiptRecoveryPhase.Failed));
            model.ActionRequested += action => action == ReceiptEditorAction.Back ? BackFromReceiptEditorAsync() : RetryReceiptMastersAsync();
            ReceiptEntry.ViewModel = model;
            ReceiptsView.RememberPosition();
            ReceiptListModel?.Suspend();
            TransferListModel?.Suspend();
            RecordContent.Visibility = Visibility.Collapsed;
            ReceiptEditorPage.Visibility = Visibility.Visible;
            PopulateReceiptEditor();
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested) { }
        catch (Exception error) when (error is AuthenticationRejectedException or AuthenticationContextChangedException)
        { if (Current()) NavigateToLogin("ログインを継続できません。もう一度ログインしてください。"); }
        catch (Exception error) when (error is TaxRateRequestException or ReceiptRequestException or CategoryRequestException or PeopleRequestException or PaymentMethodRequestException or ApiConnectionException or HttpRequestException or OperationCanceledException)
        {
            if (Current())
            {
                ReceiptOpenRetry.Visibility = Visibility.Visible;
                ReceiptOpenMessage.Text = error is ReceiptRequestException { StatusCode: HttpStatusCode.NotFound }
                ? "このレシートは開けません。参加状態と対象を確認してください。"
                : "レシートまたは税率履歴を取得できませんでした。再試行してください。";
            }
        }
        finally
        {
            if (ReferenceEquals(receiptOpening, pending))
            {
                receiptOpening = null;
                ReceiptOpenProgress.IsActive = false;
                if (receiptEditor is not null)
                    ReceiptOpenNotice.Visibility = Visibility.Collapsed;
                UpdateReceiptActions();
            }
        }
    }
    private void CancelReceiptOpening()
    {
        var pending = receiptOpening; receiptOpening = null; pending?.Cancel();
        ReceiptOpenProgress.IsActive = false; ReceiptOpenNotice.Visibility = Visibility.Collapsed; ReceiptOpenRetry.Visibility = Visibility.Collapsed;
        UpdateReceiptActions();
    }
    private void ResetReceiptEditor()
    {
        receiptAuthentication = null; receiptRecoveryPhase = ReceiptRecoveryPhase.None; receiptRecoveryLoading?.Cancel(); receiptRecoveryLoading = null;
        CancelReceiptOpening(); receiptTaxRates = null; receiptEditor = null; receiptDetailInput = null;
        ReceiptEntry.ViewModel = null;
        ReceiptEditorPage.Visibility = Visibility.Collapsed; RecordContent.Visibility = Visibility.Visible;
    }
    private void PopulateReceiptEditor()
    {
        if (receiptEditor is not { } state) return;
        receiptEditorRendering = true;
        ReceiptEntry.Populate();
        receiptEditorRendering = false;
        RefreshReceiptEditorChoices(); RenderReceiptGroups(); RenderReceiptRecovery();
    }
    private static void SetReceiptChoice(ComboBox box, string? value) => box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == value);
    private static string? ReceiptChoice(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
    private void SyncReceiptHeader()
    {
        if (!receiptEditorRendering) ReceiptEntry.SynchronizeParent();
    }
    private bool ReceiptEditorDirty => receiptEditor is { } state &&
        (state.NeedsLeaveConfirmation || receiptDetailInput?.HasChanges == true || ReceiptEntry.HasUnresolvedInput);
    private void RefreshReceiptEditorChoices()
    {
        if (receiptEditorRendering || receiptEditor is not { } state || currentLedger is null) return;
        receiptEditorRendering = true;
        var ledger = currentLedger.LedgerId; var cache = connection.Masters;
        var date = state.Header.TransactionAt is DateTime at ? DateOnly.FromDateTime(at) : (DateOnly?)null;
        bool Valid(DateOnly? from, DateOnly? to) => date is DateOnly day && (from is null || from <= day) && (to is null || to >= day);
        if (ReceiptEntry.ViewModel is { } model)
            ReceiptEntry.UpdatePaymentChoices(model.PaymentChoices(cache.People(ledger), cache.Payments(ledger), cache.MyPersonId(ledger),
                id => cache.PaymentName(ledger, id)));
        var previousCategory = ReceiptCategoryChoice.SelectedItem as ReceiptFilterChoice;
        var selectedCategory = previousCategory?.Id;
        var categories = (cache.Categories(ledger)?.Categories ?? []).Where(c => Valid(c.ValidFrom, c.ValidTo)).OrderBy(c => c.DisplayOrder).ThenBy(c => c.CategoryId)
            .Select(c => new ReceiptFilterChoice(c.CategoryId, c.Name)).ToList();
        if (receiptAuthentication is not null && previousCategory is { Id: Guid selectedId } && categories.All(c => c.Id != selectedId)) categories.Add(previousCategory);
        ReceiptCategoryChoice.ItemsSource = categories; ReceiptCategoryChoice.SelectedItem = categories.FirstOrDefault(c => c.Id == selectedCategory);
        receiptEditorRendering = false; UpdateReceiptCategoryActions();
    }
    private void RenderReceiptGroups()
    {
        if (receiptEditor is not { } state || currentLedger is null) return;
        var categories = connection.Masters.Categories(currentLedger.LedgerId)?.Categories ?? [];
        var names = categories.ToDictionary(c => c.CategoryId);
        ReceiptCategories.ItemsSource = state.Groups.OrderBy(g => names.GetValueOrDefault(g.CategoryId)?.DisplayOrder ?? int.MaxValue).ThenBy(g => g.CategoryId)
            .Select(g => new ReceiptCategoryRow(g.Key, g.CategoryId, names.GetValueOrDefault(g.CategoryId)?.Name ?? "科目名未取得")).ToArray();
        SelectReceiptGroupPresentation();
    }
    private void SelectReceiptGroupPresentation()
    {
        if (receiptEditor is not { } state) return;
        receiptEditorRendering = true;
        ReceiptCategories.SelectedItem = ReceiptCategories.Items.OfType<ReceiptCategoryRow>().FirstOrDefault(c => c.Key == state.SelectedGroupKey);
        ReceiptCategoryMemo.IsEnabled = ReceiptCategoryRemove.IsEnabled = state.SelectedGroup is not null;
        ReceiptCategoryMemo.Text = state.SelectedGroup?.Memo ?? "";
        receiptEditorRendering = false; UpdateReceiptCategoryActions(); LoadReceiptDetailForm();
    }
    private void ReceiptGroupSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (receiptEditorRendering) return;
        receiptEditorRendering = true;
        ReceiptCategories.SelectedItem = ReceiptCategories.Items.OfType<ReceiptCategoryRow>().FirstOrDefault(c => c.Key == receiptEditor?.SelectedGroupKey);
        receiptEditorRendering = false;
    }
    private void ReceiptCategoryChoiceChanged(object sender, SelectionChangedEventArgs args) => UpdateReceiptCategoryActions();
    private void UpdateReceiptCategoryActions() => ReceiptCategoryAdd.IsEnabled = !ReceiptRecoveryReadOnly && receiptEditor is { } state &&
        ReceiptCategoryChoice.SelectedItem is ReceiptFilterChoice { Id: Guid id } && state.Groups.All(g => g.CategoryId != id) &&
        currentLedger is { } ledger && connection.Masters.Categories(ledger.LedgerId)?.Categories.Any(c => c.CategoryId == id) == true;
    private async void AddReceiptCategory(object sender, RoutedEventArgs args)
    {
        if (ReceiptRecoveryReadOnly || receiptDialogOpen || receiptDetailTransition || receiptEditor is not { } state || ReceiptCategoryChoice.SelectedItem is not ReceiptFilterChoice { Id: Guid id } || !ReceiptCategoryAdd.IsEnabled) return;
        receiptDetailTransition = true;
        try
        {
            SyncReceiptGroupMemo(); if (!await ResolveReceiptDetailAsync()) return;
            state.AddCategory(id); RenderReceiptGroups(); ReceiptItemName.Focus(FocusState.Programmatic);
        }
        finally { receiptDetailTransition = false; }
    }
    private void ReceiptCategoryClicked(object sender, ItemClickEventArgs args)
    { if (args.ClickedItem is ReceiptCategoryRow row) ActivateReceiptCategory(row.Key); }
    internal async void ActivateReceiptCategory(Guid key)
    {
        if (ReceiptRecoveryReadOnly || receiptDialogOpen || receiptDetailTransition || receiptEditor?.SelectedGroupKey == key) return;
        receiptDetailTransition = true;
        try
        {
            SyncReceiptGroupMemo(); if (!await ResolveReceiptDetailAsync()) return;
            receiptEditor?.SelectGroup(key); SelectReceiptGroupPresentation(); ReceiptItemName.Focus(FocusState.Programmatic);
        }
        catch (ReceiptInputException) { ShowError("科目情報を確認できないため切り替えできません。入力は保持しています。"); }
        finally { receiptDetailTransition = false; }
    }
    private void ReceiptCategoryKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter || ReceiptEntry.IsComposing ||
            new[] { VirtualKey.Control, VirtualKey.Menu, VirtualKey.Shift }.Any(key => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down))) return;
        args.Handled = ActivateFocusedReceiptCategory();
    }
    internal bool ActivateFocusedReceiptCategory()
    {
        for (var element = FocusManager.GetFocusedElement(WorkspaceRoot.XamlRoot) as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
            if (element is ListViewItem { Content: ReceiptCategoryRow row }) { ActivateReceiptCategory(row.Key); return true; }
        return false;
    }
    private void ReceiptGroupMemoChanged(object sender, TextChangedEventArgs args) => SyncReceiptGroupMemo();
    private void SyncReceiptGroupMemo()
    {
        if (!ReceiptRecoveryReadOnly && !receiptEditorRendering && receiptEditor is { SelectedGroup: { } group } state)
        {
            var value = ReceiptMemoValue.Normalize(ReceiptCategoryMemo.Text);
            if (value != group.Memo) state.SetGroupMemo(value);
        }
    }
    private async void RemoveReceiptCategory(object sender, RoutedEventArgs args)
    {
        if (ReceiptRecoveryReadOnly || receiptDialogOpen || receiptDetailTransition || receiptEditor is not { SelectedGroup: { } group } state) return;
        receiptDetailTransition = true;
        try
        {
        if (!await ResolveReceiptDetailAsync()) return;
        var row = ReceiptCategories.Items.OfType<ReceiptCategoryRow>().Single(c => c.Key == group.Key);
        SyncReceiptGroupMemo();
        var dialog = ReceiptConfirmationDialog; dialog.XamlRoot = WorkspaceRoot.XamlRoot;
        dialog.Title = "科目を削除"; dialog.Content = $"「{row.Name}」と明細{group.Details.Count}件を削除しますか？";
        dialog.SecondaryButtonText = ""; dialog.PrimaryButtonText = "削除"; dialog.CloseButtonText = "キャンセル";
        receiptDialogOpen = true;
        try
        {
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && !ReceiptRecoveryReadOnly && ReferenceEquals(state, receiptEditor))
            { state.RemoveSelectedGroup(true); RenderReceiptGroups(); ReceiptCategoryChoice.Focus(FocusState.Programmatic); }
        }
        finally { receiptDialogOpen = false; }
        }
        finally { receiptDetailTransition = false; }
    }
    private async Task BackFromReceiptEditorAsync()
    {
        if (!await LeaveReceiptEditorAsync()) return;
        ShowPage(HomePage); RestoreReceiptListPosition();
    }
    private async Task<bool> LeaveReceiptEditorAsync()
    {
        if (receiptLeaving || receiptDialogOpen || receiptDetailTransition) return false;
        if (receiptEditor is null) { CancelReceiptOpening(); return true; }
        // TextChangedの通知を待たず、離脱直前のコントロール値を必ず保護対象にする。
        SyncReceiptHeader(); SyncReceiptGroupMemo(); SyncReceiptDetailInput();
        receiptLeaving = true;
        // ダイアログの既定ボタンへのフォーカス移動も取消操作の一部であり、日時を補正しない。
        ReceiptEntry.SuppressCompletion(true);
        try
        {
            if (ReceiptEditorDirty)
            {
                var dialog = ReceiptConfirmationDialog; dialog.XamlRoot = WorkspaceRoot.XamlRoot;
                dialog.Title = "未保存の内容があります"; dialog.Content = "保存はまだ利用できません。入力内容を破棄して移動しますか？";
                dialog.SecondaryButtonText = ""; dialog.PrimaryButtonText = ReceiptRecoveryReadOnly ? "破棄して閉じる" : "破棄して移動"; dialog.CloseButtonText = "編集に戻る";
                receiptDialogOpen = true;
                try { if (await dialog.ShowAsync() != ContentDialogResult.Primary || receiptRecoveryPhase == ReceiptRecoveryPhase.Login) return false; }
                finally { receiptDialogOpen = false; }
            }
            ResetReceiptEditor(); receiptRestorePending = true; return true;
        }
        finally { ReceiptEntry.SuppressCompletion(false); receiptLeaving = false; }
    }
    private void RestoreReceiptListPosition() => ReceiptsView.RestorePosition();
    private void ResizeReceiptDetails(bool narrow)
    {
        var grid = ReceiptCategoryArea;
        for (var i = 0; i < grid.Children.Count; i++)
        {
            Grid.SetRow((FrameworkElement)grid.Children[i], narrow ? i : 0);
            Grid.SetColumn((FrameworkElement)grid.Children[i], narrow ? 0 : i);
            Grid.SetColumnSpan((FrameworkElement)grid.Children[i], narrow ? grid.ColumnDefinitions.Count : 1);
        }
    }
}
