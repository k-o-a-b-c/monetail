using System.Globalization;
using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Monetail.WinUI.Input;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Input;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transfers;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
    private TransferEditorState? transferEditor;
    private CancellationTokenSource? transferOpening;
    private Guid? transferOpenRetry;
    private bool transferRendering, transferDialogOpen, transferLeaving, transferCloseApproved, transferStopped, transferSending, transferUnknown, transferRestoringSaved, transferRestorePending;
    private TransferWriteRequest? transferPending;
    private TransferSnapshot? transferPendingSnapshot;
    private FormKeyboard transferKeyboard = null!;
    private double transferReturnOffset;
    private Guid? transferSavedSelection;
    private bool RecordEditorActive => receiptEditor is not null || transferEditor is not null;
    private PaymentMethodsInfo? TransferMethods => currentLedger is { } ledger ? connection.Masters.Payments(ledger.LedgerId) : null;
    private PeopleInfo? TransferPeople => currentLedger is { } ledger ? connection.Masters.People(ledger.LedgerId) : null;
    private bool TransferFrozen => transferSending || transferPending is not null || transferStopped || transferEditor?.IsReadOnly == true;
    private void InitializeTransferEditor()
    {
        WorkspaceRoot.Children.Remove(TransferConfirmationDialog);
        transferKeyboard = new FormKeyboard(TransferEditorPage, TransferSave, () => SaveTransferAsync(false), TransferDate, TransferTime, TransferAmount, TransferMemo);
        TransferName.Loaded += (_, _) =>
        {
            if (transferNameInputObserved || Descendant<TextBox>(TransferName) is not { } input) return;
            transferNameInputObserved = true; transferKeyboard.ObserveInput(input);
            input.TextCompositionStarted += (_, _) => transferNameCompositionPending = true;
            input.TextCompositionEnded += (_, _) => DispatcherQueue.TryEnqueue(() => { if (!transferKeyboard.IsComposing && transferNameCompositionPending) { transferNameCompositionPending = false; _ = SearchTransferNamesAsync(true); } });
        };
        TransferDate.IsCancellationTarget = TransferTime.IsCancellationTarget = IsTransferLeaveTarget;
        AppWindow.Closing += async (_, args) =>
        {
            if (transferCloseApproved || transferEditor is null) return;
            args.Cancel = true;
            if (await LeaveTransferEditorAsync()) { transferCloseApproved = true; Close(); }
        };
    }
    private bool IsTransferLeaveTarget(DependencyObject? element)
    {
        for (; element is not null; element = VisualTreeHelper.GetParent(element))
            if (ReferenceEquals(element, TransferBack) || ReferenceEquals(element, SettingsButton) || ReferenceEquals(element, RecordButton) || ReferenceEquals(element, LedgerSelector)) return true;
        return false;
    }
    private async Task<bool> LeaveRecordEditorAsync() => await LeaveTransferEditorAsync() && await LeaveReceiptEditorAsync();
    private async void NewTransfer(object sender, RoutedEventArgs args) => await OpenTransferEditorAsync(null);
    private async void TransferDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        for (var element = args.OriginalSource as DependencyObject; element is not null && !ReferenceEquals(element, TransferList); element = VisualTreeHelper.GetParent(element))
            if (element is FrameworkElement { DataContext: TransferRow row })
            { args.Handled = true; await OpenTransferEditorAsync(row.Transfer.TransferId); return; }
    }
    internal async Task OpenTransferEditorAsync(Guid? id)
    {
        if (busy || closed || ManagementDialogOpen || transferOpening is not null || receiptOpening is not null || RecordEditorActive || account is null || currentLedger is null || HomePage.Visibility != Visibility.Visible) return;
        var ledger = currentLedger.LedgerId; var person = account.AuthUserId; var api = connection.Api; var session = connection.Session;
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); transferOpening = pending; transferOpenRetry = id;
        TransferOpenNotice.Visibility = Visibility.Visible; TransferOpenProgress.IsActive = true; TransferOpenRetry.Visibility = Visibility.Collapsed; TransferOpenMessage.Text = "振替を読み込み中…";
        TransferRegisterPlaceholder.IsEnabled = false;
        bool Current() => !closed && ReferenceEquals(transferOpening, pending) && !pending.IsCancellationRequested && currentLedger?.LedgerId == ledger && account?.AuthUserId == person && HomePage.Visibility == Visibility.Visible && !RecordEditorActive;
        try
        {
            var saved = id is Guid value ? await new TransfersWorkspace(api, session, ledger).GetAsync(value, pending.Token) : null;
            await connection.Masters.GetPeopleAsync(ledger, ct => new PeopleWorkspace(api, session, ledger).ListAsync(ct), pending.Token);
            await connection.Masters.GetPaymentsAsync(ledger, ct => new PaymentMethodsWorkspace(api, session, ledger).ListAsync(ct), pending.Token);
            if (!Current() || TransferMethods is not { } methods) return;
            if (saved is null && !TransferEditorRules.CanCreate(methods)) { TransferOpenMessage.Text = "振替を登録する権限がありません。"; return; }
            transferEditor = saved is null ? TransferEditorState.Create(DateOnly.FromDateTime(DateTime.Now)) : TransferEditorState.Edit(saved);
            if (saved is not null && (!TransferEditorRules.CanUse(methods, saved.FromPaymentMethodId) || !TransferEditorRules.CanUse(methods, saved.ToPaymentMethodId))) transferEditor.EnterReadOnly(TransferReadOnlyReason.Forbidden);
            transferReturnOffset = transferScroller?.VerticalOffset ?? 0; receiptReturnOffset = receiptScroller?.VerticalOffset ?? 0;
            transferState?.Suspend(); receiptState?.Suspend(); RecordContent.Visibility = Visibility.Collapsed; TransferEditorPage.Visibility = Visibility.Visible;
            transferStopped = transferUnknown = false; transferPending = null; transferPendingSnapshot = null; ResetTransferSuggestions();
            PopulateTransferEditor(); TransferName.Focus(FocusState.Programmatic);
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested) { }
        catch (Exception error) when (error is AuthenticationRejectedException or AuthenticationContextChangedException)
        { if (Current()) ShowLogin("ログインを継続できません。もう一度ログインしてください。"); }
        catch (Exception error) when (error is TransferRequestException or PeopleRequestException or PaymentMethodRequestException or ApiConnectionException or HttpRequestException or OperationCanceledException)
        {
            if (Current())
            {
                TransferOpenMessage.Text = error is TransferRequestException { StatusCode: HttpStatusCode.NotFound } ? "振替が削除されたか、アクセスできなくなりました。" : "振替またはマスタ情報を取得できませんでした。";
                TransferOpenRetry.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            if (ReferenceEquals(transferOpening, pending))
            {
                transferOpening = null; TransferOpenProgress.IsActive = false;
                if (transferEditor is not null) TransferOpenNotice.Visibility = Visibility.Collapsed;
                TransferRegisterPlaceholder.IsEnabled = TransferMethods is { } methods && TransferEditorRules.CanCreate(methods);
            }
        }
    }
    private async void RetryTransferOpening(object sender, RoutedEventArgs args) => await OpenTransferEditorAsync(transferOpenRetry);
    private void CancelTransferOpening()
    {
        var pending = transferOpening; transferOpening = null; pending?.Cancel();
        TransferOpenNotice.Visibility = Visibility.Collapsed; TransferOpenProgress.IsActive = false;
    }
    private void ResetTransferEditor()
    {
        CancelTransferOpening(); CancelTransferSuggestions(); transferAutofillCancellation?.Cancel(); transferAutofillCancellation?.Dispose(); transferAutofillCancellation = null;
        transferEditor = null; transferPending = null; transferPendingSnapshot = null; transferStopped = transferUnknown = false;
        TransferEditorPage.Visibility = Visibility.Collapsed; RecordContent.Visibility = Visibility.Visible;
    }
    private void PopulateTransferEditor()
    {
        if (transferEditor is not { } state) return;
        transferRendering = true;
        TransferEditorTitle.Text = state.IsExisting ? "振替の編集" : "振替の登録";
        TransferDate.Text = DateTimeInput.FormatDate(state.Draft.TransactionDate); TransferTime.Text = DateTimeInput.FormatTime(state.Draft.TransactionTime);
        TransferName.Text = TransferNameReadOnly.Text = state.Draft.Name; TransferAmount.Text = state.Draft.Amount?.ToString(CultureInfo.InvariantCulture) ?? ""; TransferMemo.Text = state.Draft.Memo ?? "";
        TransferSave.Content = state.IsExisting ? "更新" : "登録"; TransferDelete.Visibility = TransferReuse.Visibility = state.IsExisting ? Visibility.Visible : Visibility.Collapsed;
        TransferEditorNotice.IsOpen = false; transferRendering = false; RefreshTransferEditorChoices(); RenderTransferEditor();
    }
    private void SyncTransferInput()
    {
        if (transferRendering || TransferFrozen || transferEditor is not { } state) return;
        var date = DateTime.TryParseExact(TransferDate.Text, "yyyy/MM/dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? DateOnly.FromDateTime(day) : (DateOnly?)null;
        var time = TimeOnly.TryParseExact(TransferTime.Text, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var clock) ? clock : (TimeOnly?)null;
        state.Draft.SetDate(date, date is null ? TransferDate.Text : null); state.Draft.SetTime(time, time is null ? TransferTime.Text : null);
        state.Draft.SetAmount(int.TryParse(TransferAmount.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) && amount > 0 ? amount : null,
            TransferAmount.Text.Length == 0 || amount > 0 ? null : TransferAmount.Text);
        state.Draft.Memo = TransferMemo.Text is "" ? null : TransferMemo.Text;
    }
    private void TransferTextChanged(object sender, TextChangedEventArgs args)
    { if (transferRendering) return; SyncTransferInput(); if (ReferenceEquals(sender, TransferDate)) RefreshTransferEditorChoices(); RenderTransferEditor(); }
    private void TransferPaymentChanged(object sender, SelectionChangedEventArgs args)
    {
        if (transferRendering || TransferFrozen || transferEditor is not { } state || (sender as ComboBox)?.SelectedItem is not ReceiptFilterChoice { Id: Guid id }) return;
        if (ReferenceEquals(sender, TransferFrom)) state.SelectFrom(id); else state.SelectTo(id);
        RenderTransferEditor(); if (ReferenceEquals(sender, TransferFrom)) _ = AutofillTransferAsync();
    }
    private void RefreshTransferEditorChoices()
    {
        if (transferEditor is not { } state || TransferMethods is not { } methods || TransferPeople is not { } people || currentLedger is null) return;
        transferRendering = true;
        var day = state.Draft.UnresolvedDateText is null ? state.Draft.TransactionDate : null;
        var order = people.People.OrderByDescending(p => p.PersonId == methods.MyPersonId).ThenBy(p => p.DisplayOrder).Select((p, i) => (p.PersonId, i)).ToDictionary(p => p.PersonId, p => p.i);
        var available = methods.PaymentMethods.Where(p => TransferEditorRules.CanUse(methods, p.PaymentMethodId) && day is DateOnly date && TransferEditorRules.IsActive(date, p.ValidFrom, p.ValidTo) && people.People.Any(person => person.PersonId == p.OwnerPersonId && TransferEditorRules.IsActive(date, person.ValidFrom, person.ValidTo)))
            .OrderBy(p => order.GetValueOrDefault(p.OwnerPersonId, int.MaxValue)).ThenBy(p => p.DisplayOrder).ToArray();
        void Fill(ComboBox combo, Guid? selected)
        {
            var choices = available.Select(p => new ReceiptFilterChoice(p.PaymentMethodId, connection.Masters.PaymentName(currentLedger.LedgerId, p.PaymentMethodId))).ToList();
            if (selected is Guid id && choices.All(c => c.Id != id)) choices.Add(new(id, connection.Masters.PaymentName(currentLedger.LedgerId, id)));
            combo.ItemsSource = choices; combo.SelectedItem = choices.FirstOrDefault(p => p.Id == selected);
        }
        Fill(TransferFrom, state.Draft.From.PaymentMethodId); Fill(TransferTo, state.Draft.To.PaymentMethodId);
        TransferFromReadOnly.Text = state.Draft.From.PaymentMethodId is Guid from ? connection.Masters.PaymentName(currentLedger.LedgerId, from) : "";
        TransferToReadOnly.Text = state.Draft.To.PaymentMethodId is Guid to ? connection.Masters.PaymentName(currentLedger.LedgerId, to) : "";
        transferRendering = false; RenderTransferEditor();
    }
    private TransferSavePreparation PrepareTransfer(bool reuse)
    {
        if (transferEditor is not { } state || TransferMethods is not { } methods || TransferPeople is not { } people) throw new TransferInputException("master", "マスタ情報を取得してください。");
        var prepared = state.PrepareSave(reuse ? TransferSaveKind.ReuseRegistration : state.IsExisting ? TransferSaveKind.Update : TransferSaveKind.Register);
        TransferEditorRules.Validate(prepared.Snapshot, people, methods); return prepared;
    }
    private void RenderTransferEditor()
    {
        if (transferEditor is not { } state) return;
        var frozen = TransferFrozen;
        TransferDate.IsReadOnly = TransferTime.IsReadOnly = TransferAmount.IsReadOnly = TransferMemo.IsReadOnly = frozen;
        TransferNameReadOnly.Text = state.Draft.Name;
        if (currentLedger is { } ledger)
        {
            TransferFromReadOnly.Text = state.Draft.From.PaymentMethodId is Guid from ? connection.Masters.PaymentName(ledger.LedgerId, from) : "";
            TransferToReadOnly.Text = state.Draft.To.PaymentMethodId is Guid to ? connection.Masters.PaymentName(ledger.LedgerId, to) : "";
        }
        TransferName.Visibility = frozen ? Visibility.Collapsed : Visibility.Visible; TransferNameReadOnly.Visibility = frozen ? Visibility.Visible : Visibility.Collapsed;
        TransferFrom.Visibility = TransferTo.Visibility = frozen ? Visibility.Collapsed : Visibility.Visible;
        TransferFromReadOnly.Visibility = TransferToReadOnly.Visibility = frozen ? Visibility.Visible : Visibility.Collapsed;
        TransferSave.IsEnabled = TransferReuse.IsEnabled = false;
        if (!frozen) { try { PrepareTransfer(false); TransferSave.IsEnabled = true; TransferReuse.IsEnabled = state.IsExisting; } catch (TransferInputException) { } }
        TransferDelete.IsEnabled = state.IsExisting && !frozen;
        TransferWriteProgress.IsActive = transferSending; TransferWriteProgress.Visibility = transferSending ? Visibility.Visible : Visibility.Collapsed;
        TransferWriteRetry.Visibility = transferUnknown ? Visibility.Visible : Visibility.Collapsed; TransferWriteRetry.IsEnabled = transferUnknown && !transferSending;
        if (state.IsReadOnly && !TransferEditorNotice.IsOpen) TransferNotice("この振替は読み取り専用です。", InfoBarSeverity.Informational);
        TransferValidity.Text = "";
        if (!state.IsReadOnly && state.Draft.TransactionAt is DateTime && state.Draft.From.PaymentMethodId is Guid && state.Draft.To.PaymentMethodId is Guid && TransferPeople is not null && TransferMethods is not null)
            try { var snapshot = new TransferSnapshot(state.Draft.TransactionAt.Value, state.Draft.Name, state.Draft.From.PaymentMethodId.Value, state.Draft.To.PaymentMethodId.Value, state.Draft.Amount ?? 1, state.Draft.Memo); TransferEditorRules.Validate(snapshot, TransferPeople, TransferMethods); }
            catch (TransferInputException error) { TransferValidity.Text = error.Message; }
        if (!frozen && TransferValidity.Text.Length == 0)
        {
            if (state.Draft.Name.Length > 100 || state.Draft.Name.Length > 0 && string.IsNullOrWhiteSpace(state.Draft.Name)) TransferValidity.Text = "振替名は空白のみを避け、100文字以内で入力してください。";
            else if (TransferAmount.Text.Length > 0 && (state.Draft.Amount is null || state.Draft.UnresolvedAmountText is not null)) TransferValidity.Text = "金額は1〜2,147,483,647の整数で入力してください。";
            else if (TransferMemo.Text.Length > 100 || TransferMemo.Text.IndexOfAny(['\r','\n','\v','\f','\u0085','\u2028','\u2029']) >= 0) TransferValidity.Text = "メモは改行なし・100文字以内で入力してください。";
            else if (state.Draft.From.PaymentMethodId is Guid from && state.Draft.To.PaymentMethodId == from) TransferValidity.Text = "振替元と振替先は異なる支払方法を選択してください。";
        }
        TransferValidity.Visibility = TransferValidity.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
    private void TransferNotice(string message, InfoBarSeverity severity = InfoBarSeverity.Error)
    { TransferEditorNotice.Message = message; TransferEditorNotice.Severity = severity; TransferEditorNotice.IsOpen = true; }
    private async void TransferBackClicked(object sender, RoutedEventArgs args)
    { if (await LeaveTransferEditorAsync()) { ShowPage(HomePage); RestoreTransferPosition(); } }
    private void RestoreTransferPosition() => DispatcherQueue.TryEnqueue(() => { if (!RecordEditorActive && HomePage.Visibility == Visibility.Visible) transferScroller?.ChangeView(null, transferReturnOffset, null, true); });
}
