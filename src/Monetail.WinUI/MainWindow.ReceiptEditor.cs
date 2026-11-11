using System.Globalization;
using System.Net;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Monetail.WinUI.Input;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Input;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Client.Transport;
using Windows.System;
using Windows.UI.Core;

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
    private ReceiptEditorState? receiptEditor;
    private ReceiptParentInputState? receiptParentInput;
    private CancellationTokenSource? receiptOpening;
    private bool receiptEditorRendering, receiptDialogOpen, receiptLeaving, receiptCloseApproved, receiptRestorePending;
    private double receiptReturnOffset;
    private string? receiptInitialLocation, receiptInitialMemo;
    private string receiptInitialMemoText = "";
    private readonly Dictionary<Guid, string?> receiptInitialGroupMemos = [];
    private readonly Dictionary<Guid, string> receiptInitialGroupMemoTexts = [];
    private FormKeyboard receiptEditorKeyboard = null!;

    private void InitializeReceiptEditor()
    {
        WorkspaceRoot.Children.Remove(ReceiptConfirmationDialog);
        receiptEditorKeyboard = new FormKeyboard(ReceiptEditorPage, null, null, ReceiptDate, ReceiptTime, ReceiptShopName,
            ReceiptShopLocation, ReceiptInterval, ReceiptMemo, ReceiptCategoryMemo, ReceiptItemName, ReceiptItemPrice, ReceiptItemQuantity,
            ReceiptItemDiscount, ReceiptItemRateDiscount, ReceiptItemMemo);
        ReceiptDate.IsCancellationTarget = ReceiptTime.IsCancellationTarget = IsReceiptLeaveTarget;
        AppWindow.Closing += async (_, args) =>
        {
            if (receiptCloseApproved || receiptEditor is null) return;
            args.Cancel = true;
            if (await LeaveReceiptEditorAsync()) { receiptCloseApproved = true; Close(); }
        };
    }
    private bool IsReceiptLeaveTarget(DependencyObject? element)
    {
        for (; element is not null; element = VisualTreeHelper.GetParent(element))
            if (ReferenceEquals(element, ReceiptBack) || ReferenceEquals(element, SettingsButton) ||
                ReferenceEquals(element, RecordButton) || ReferenceEquals(element, LedgerSelector)) return true;
        return false;
    }
    private async void NewReceipt(object sender, RoutedEventArgs args) => await OpenReceiptEditorAsync(null);
    private async void ReceiptDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        for (var element = args.OriginalSource as DependencyObject; element is not null && !ReferenceEquals(element, ReceiptList); element = VisualTreeHelper.GetParent(element))
            if (element is FrameworkElement { DataContext: ReceiptRow row })
            { args.Handled = true; await OpenReceiptEditorAsync(row.Receipt.ReceiptId); return; }
    }
    // 一要求だけを受理。取消後に通信が完了しても同一要求・本人・家計簿・画面を照合して適用しない。
    internal async Task OpenReceiptEditorAsync(Guid? receiptId)
    {
        if (busy || closed || ManagementDialogOpen || receiptOpening is not null || receiptEditor is not null || account is null || currentLedger is null || HomePage.Visibility != Visibility.Visible) return;
        var ledger = currentLedger.LedgerId; var person = account.AuthUserId;
        var api = connection.Api; var session = connection.Session;
        var initial = new ReceiptHeaderDraft { TransactionAt = DateTime.Today, ShopLocation = "", Memo = "", TaxInclusionMode = 0, TaxRoundingMode = "floor" };
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        receiptOpening = pending; NewReceiptButton.IsEnabled = false;
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
            ReceiptCategoryDefaults Defaults(Guid category, DateTime? date)
            {
                var master = connection.Masters.Categories(ledger)?.Categories.FirstOrDefault(c => c.CategoryId == category)
                    ?? throw new ReceiptInputException("categoryId", "科目情報を取得できませんでした。");
                // 税率履歴は後続工程で接続。履歴未取得を非課税として代用しない。
                return new(master.IncomeExpense, ReceiptTaxSelection.Unselected);
            }
            receiptEditor = info is null ? ReceiptEditorState.Create(initial, Defaults) : ReceiptEditorState.Edit(info, Defaults);
            receiptParentInput = new(receiptEditor.Header, info is not null);
            receiptInitialLocation = receiptEditor.Header.ShopLocation; receiptInitialMemo = receiptEditor.Header.Memo;
            receiptReturnOffset = receiptScroller?.VerticalOffset ?? 0;
            receiptState?.Suspend(); RecordContent.Visibility = Visibility.Collapsed; ReceiptEditorPage.Visibility = Visibility.Visible;
            PopulateReceiptEditor();
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested) { }
        catch (Exception error) when (error is AuthenticationRejectedException or AuthenticationContextChangedException)
        { if (Current()) ShowLogin("ログインを継続できません。もう一度ログインしてください。"); }
        catch (Exception error) when (error is ReceiptRequestException or CategoryRequestException or PeopleRequestException or PaymentMethodRequestException or ApiConnectionException or HttpRequestException or OperationCanceledException)
        {
            if (Current()) ReceiptOpenMessage.Text = error is ReceiptRequestException { StatusCode: HttpStatusCode.NotFound }
                ? "このレシートは開けません。参加状態と対象を確認してください。"
                : "レシートを開けませんでした。登録ボタンまたは行のダブルクリックで再試行してください。";
        }
        finally
        {
            if (ReferenceEquals(receiptOpening, pending))
            { receiptOpening = null; ReceiptOpenProgress.IsActive = false; if (receiptEditor is not null) ReceiptOpenNotice.Visibility = Visibility.Collapsed; NewReceiptButton.IsEnabled = account is not null; }
        }
    }
    private void CancelReceiptOpening()
    {
        var pending = receiptOpening; receiptOpening = null; pending?.Cancel();
        ReceiptOpenProgress.IsActive = false; ReceiptOpenNotice.Visibility = Visibility.Collapsed;
        NewReceiptButton.IsEnabled = account is not null;
    }
    private void ResetReceiptEditor()
    {
        CancelReceiptOpening(); receiptEditor = null; receiptParentInput = null; receiptDetailInput = null; receiptInitialGroupMemos.Clear(); receiptInitialGroupMemoTexts.Clear();
        ReceiptEditorPage.Visibility = Visibility.Collapsed; RecordContent.Visibility = Visibility.Visible;
    }
    private void PopulateReceiptEditor()
    {
        if (receiptEditor is not { } state) return;
        receiptEditorRendering = true;
        var header = state.Header;
        ReceiptEditorTitle.Text = state.ReceiptId is null ? "レシートの登録" : "レシートの編集";
        ReceiptDate.Text = DateTimeInput.FormatDate(header.TransactionAt is DateTime at ? DateOnly.FromDateTime(at) : null);
        ReceiptTime.Text = DateTimeInput.FormatTime(header.TransactionAt is DateTime time ? TimeOnly.FromDateTime(time) : null);
        ReceiptShopName.Text = header.ShopName; ReceiptShopLocation.Text = header.ShopLocation ?? ""; ReceiptMemo.Text = header.Memo ?? "";
        receiptInitialMemoText = ReceiptMemo.Text;
        RenderReceiptTaxValues();
        ReceiptIntervalError.Visibility = Visibility.Collapsed;
        ReceiptInterval.Text = header.RecurrenceInterval?.ToString(CultureInfo.InvariantCulture) ?? ""; SetReceiptChoice(ReceiptUnit, header.RecurrenceUnit ?? "Month");
        ReceiptRegister.Visibility = state.ReceiptId is null ? Visibility.Visible : Visibility.Collapsed;
        ReceiptUpdate.Visibility = ReceiptReuse.Visibility = state.ReceiptId is null ? Visibility.Collapsed : Visibility.Visible;
        receiptEditorRendering = false; RefreshReceiptEditorChoices(); RenderReceiptGroups();
    }
    internal void RenderReceiptTaxValues()
    {
        if (receiptEditor is not { } state) return;
        var rendering = receiptEditorRendering; receiptEditorRendering = true;
        try
        {
            ReceiptTaxIncluded.IsChecked = state.Header.TaxInclusionMode == 0; ReceiptTaxExcluded.IsChecked = state.Header.TaxInclusionMode == 1;
            ReceiptTaxFloor.IsChecked = state.Header.TaxRoundingMode == "floor"; ReceiptTaxHalfUp.IsChecked = state.Header.TaxRoundingMode == "half_up";
        }
        finally { receiptEditorRendering = rendering; }
    }
    private static void SetReceiptChoice(ComboBox box, string? value) => box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == value);
    private static string? ReceiptChoice(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
    private void ReceiptParentTextChanged(object sender, TextChangedEventArgs args) => SyncReceiptHeader();
    private void ReceiptParentChoiceChanged(object sender, SelectionChangedEventArgs args) => SyncReceiptHeader();
    private void ReceiptTaxChecked(object sender, RoutedEventArgs args) => SyncReceiptHeader();
    private void ReceiptTaxSelected(object sender, RoutedEventArgs args)
    {
        if (receiptEditorRendering || receiptParentInput is null || sender is not RadioButton radio) return;
        // 標準Clickはマウスとキーボードの明示選択で発火し、選択済みの再操作も含む。Checkedだけでは由来を変えない。
        if (radio.GroupName == "ReceiptInclusion") receiptParentInput.SelectTaxInclusion(int.Parse((string)radio.Tag, CultureInfo.InvariantCulture));
        else receiptParentInput.SelectTaxRounding((string)radio.Tag);
        SyncReceiptHeader();
    }
    private void SyncReceiptHeader()
    {
        if (receiptEditorRendering || receiptEditor is not { } state) return;
        var header = state.Header; var previousDate = header.TransactionAt?.Date; var previousPayment = header.PaymentMethodId;
        if (DateOnly.TryParseExact(ReceiptDate.Text, "yyyy/MM/dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) &&
            TimeOnly.TryParseExact(ReceiptTime.Text, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) header.TransactionAt = date.ToDateTime(time);
        header.ShopName = ReceiptShopName.Text;
        header.ShopLocation = ReceiptOptionalText(ReceiptShopLocation.Text, receiptInitialLocation);
        header.Memo = ReceiptOptionalText(ReceiptMemo.Text, receiptInitialMemo, receiptInitialMemoText);
        if (ReceiptEditorPayment.SelectedItem is ReceiptFilterChoice payment) header.PaymentMethodId = payment.Id;
        header.TaxInclusionMode = ReceiptTaxIncluded.IsChecked == true ? 0 : ReceiptTaxExcluded.IsChecked == true ? 1 : null;
        header.TaxRoundingMode = ReceiptTaxFloor.IsChecked == true ? "floor" : ReceiptTaxHalfUp.IsChecked == true ? "half_up" : null;
        var recurrence = ReceiptRecurrenceInput.Parse(ReceiptInterval.Text, ReceiptChoice(ReceiptUnit));
        // 不正文字列はUIに保持し、最後の有効なモデル値を「周期なし」へ書き換えない。
        if (recurrence.IsValid) { header.RecurrenceInterval = recurrence.Interval; header.RecurrenceUnit = recurrence.Unit; }
        ReceiptIntervalError.Visibility = recurrence.IsValid ? Visibility.Collapsed : Visibility.Visible;
        if (previousDate != header.TransactionAt?.Date || previousPayment != header.PaymentMethodId) RefreshReceiptEditorChoices();
    }
    // 表示時の改行正規化やNULL/空文字の差を、利用者の変更とみなさない。元の表示へ戻した場合も保存値を復元する。
    private static string? ReceiptOptionalText(string text, string? initial, string? initialDisplay = null) =>
        text == (initialDisplay ?? initial ?? "") ? initial : text;
    private bool ReceiptEditorDirty => receiptEditor is { } state && (state.NeedsLeaveConfirmation || receiptDetailInput?.HasChanges == true ||
        ReceiptDate.Text != DateTimeInput.FormatDate(state.Header.TransactionAt is DateTime at ? DateOnly.FromDateTime(at) : null) ||
        ReceiptTime.Text != DateTimeInput.FormatTime(state.Header.TransactionAt is DateTime time ? TimeOnly.FromDateTime(time) : null) ||
        !ReceiptRecurrenceInput.Parse(ReceiptInterval.Text, ReceiptChoice(ReceiptUnit)).IsValid);
    private void RefreshReceiptEditorChoices()
    {
        if (receiptEditorRendering || receiptEditor is not { } state || currentLedger is null) return;
        receiptEditorRendering = true;
        var ledger = currentLedger.LedgerId; var cache = connection.Masters;
        var people = cache.People(ledger)?.People ?? []; var mine = cache.MyPersonId(ledger);
        var owners = people.OrderByDescending(p => p.PersonId == mine).ThenBy(p => p.DisplayOrder).Select((p, i) => (p.PersonId, i)).ToDictionary(p => p.PersonId, p => p.i);
        var date = state.Header.TransactionAt is DateTime at ? DateOnly.FromDateTime(at) : (DateOnly?)null;
        var methods = (cache.Payments(ledger)?.PaymentMethods ?? []).OrderBy(p => owners.GetValueOrDefault(p.OwnerPersonId, int.MaxValue)).ThenBy(p => p.DisplayOrder).ThenBy(p => p.PaymentMethodId);
        bool Valid(DateOnly? from, DateOnly? to) => date is DateOnly day && (from is null || from <= day) && (to is null || to >= day);
        var choices = methods.Where(p => p.PaymentMethodId == state.Header.PaymentMethodId || Valid(p.ValidFrom, p.ValidTo))
            .Select(p => new ReceiptFilterChoice(p.PaymentMethodId, cache.PaymentName(ledger, p.PaymentMethodId))).ToList();
        if (state.Header.PaymentMethodId is Guid selected && choices.All(c => c.Id != selected)) choices.Add(new(selected, "支払方法名未取得"));
        ReceiptEditorPayment.ItemsSource = choices; ReceiptEditorPayment.SelectedItem = choices.FirstOrDefault(p => p.Id == state.Header.PaymentMethodId);
        var method = methods.FirstOrDefault(p => p.PaymentMethodId == state.Header.PaymentMethodId);
        ReceiptPaymentWarning.Visibility = state.Header.PaymentMethodId is not null && (method is null || !Valid(method.ValidFrom, method.ValidTo)) ? Visibility.Visible : Visibility.Collapsed;
        ReceiptPaymentWarning.Text = "選択中の支払方法は取引日に利用できません。入力値は保持しています。";
        var selectedCategory = (ReceiptCategoryChoice.SelectedItem as ReceiptFilterChoice)?.Id;
        var categories = (cache.Categories(ledger)?.Categories ?? []).Where(c => Valid(c.ValidFrom, c.ValidTo)).OrderBy(c => c.DisplayOrder).ThenBy(c => c.CategoryId)
            .Select(c => new ReceiptFilterChoice(c.CategoryId, c.Name)).ToArray();
        ReceiptCategoryChoice.ItemsSource = categories; ReceiptCategoryChoice.SelectedItem = categories.FirstOrDefault(c => c.Id == selectedCategory);
        receiptEditorRendering = false; UpdateReceiptCategoryActions();
    }
    private void RenderReceiptGroups()
    {
        if (receiptEditor is not { } state || currentLedger is null) return;
        var categories = connection.Masters.Categories(currentLedger.LedgerId)?.Categories ?? [];
        var names = categories.ToDictionary(c => c.CategoryId);
        foreach (var group in state.Groups) receiptInitialGroupMemos.TryAdd(group.Key, group.Memo);
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
        if (state.SelectedGroupKey is Guid groupKey) receiptInitialGroupMemoTexts.TryAdd(groupKey, ReceiptCategoryMemo.Text);
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
    private void UpdateReceiptCategoryActions() => ReceiptCategoryAdd.IsEnabled = receiptEditor is { } state &&
        ReceiptCategoryChoice.SelectedItem is ReceiptFilterChoice { Id: Guid id } && state.Groups.All(g => g.CategoryId != id);
    private async void AddReceiptCategory(object sender, RoutedEventArgs args)
    {
        if (receiptDialogOpen || receiptDetailTransition || receiptEditor is not { } state || ReceiptCategoryChoice.SelectedItem is not ReceiptFilterChoice { Id: Guid id } || !ReceiptCategoryAdd.IsEnabled) return;
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
        if (receiptDialogOpen || receiptDetailTransition || receiptEditor?.SelectedGroupKey == key) return;
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
        if (args.Key != VirtualKey.Enter || receiptEditorKeyboard.IsComposing ||
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
        if (!receiptEditorRendering && receiptEditor is { SelectedGroup: { } group } state)
        {
            var value = ReceiptOptionalText(ReceiptCategoryMemo.Text, receiptInitialGroupMemos.GetValueOrDefault(group.Key), receiptInitialGroupMemoTexts.GetValueOrDefault(group.Key));
            if (value != group.Memo) state.SetGroupMemo(value);
        }
    }
    private async void RemoveReceiptCategory(object sender, RoutedEventArgs args)
    {
        if (receiptDialogOpen || receiptDetailTransition || receiptEditor is not { SelectedGroup: { } group } state) return;
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
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && ReferenceEquals(state, receiptEditor))
            { state.RemoveSelectedGroup(true); RenderReceiptGroups(); ReceiptCategoryChoice.Focus(FocusState.Programmatic); }
        }
        finally { receiptDialogOpen = false; }
        }
        finally { receiptDetailTransition = false; }
    }
    private async void BackFromReceiptEditor(object sender, RoutedEventArgs args)
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
        ReceiptDate.SuppressCompletion = ReceiptTime.SuppressCompletion = true;
        try
        {
            if (ReceiptEditorDirty)
            {
                var dialog = ReceiptConfirmationDialog; dialog.XamlRoot = WorkspaceRoot.XamlRoot;
                dialog.Title = "未保存の内容があります"; dialog.Content = "保存はまだ利用できません。入力内容を破棄して移動しますか？";
                dialog.SecondaryButtonText = ""; dialog.PrimaryButtonText = "破棄して移動"; dialog.CloseButtonText = "編集に戻る";
                receiptDialogOpen = true;
                try { if (await dialog.ShowAsync() != ContentDialogResult.Primary) return false; }
                finally { receiptDialogOpen = false; }
            }
            ResetReceiptEditor(); receiptRestorePending = true; return true;
        }
        finally { ReceiptDate.SuppressCompletion = ReceiptTime.SuppressCompletion = false; receiptLeaving = false; }
    }
    private void RestoreReceiptListPosition() => DispatcherQueue.TryEnqueue(() =>
    { if (!closed && receiptEditor is null && HomePage.Visibility == Visibility.Visible) receiptScroller?.ChangeView(null, receiptReturnOffset, null, true); });
    private void ReceiptEditorSizeChanged(object sender, SizeChangedEventArgs args)
    {
        var narrow = args.NewSize.Width < 780;
        foreach (var grid in new[] { ReceiptHeaderRow1, ReceiptHeaderRow2, ReceiptHeaderRow3, ReceiptHeaderRow4, ReceiptCategoryArea })
            for (var i = 0; i < grid.Children.Count; i++)
            { Grid.SetRow((FrameworkElement)grid.Children[i], narrow ? i : 0); Grid.SetColumn((FrameworkElement)grid.Children[i], narrow ? 0 : i); Grid.SetColumnSpan((FrameworkElement)grid.Children[i], narrow ? grid.ColumnDefinitions.Count : 1); }
    }
}
