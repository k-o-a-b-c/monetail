using System.Net;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transfers;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
    private async Task<ContentDialogResult> ConfirmTransferAsync(string title, string message, string primary, string secondary = "", string close = "キャンセル")
    {
        if (transferDialogOpen) return ContentDialogResult.None;
        transferDialogOpen = true; TransferDate.SuppressCompletion = TransferTime.SuppressCompletion = true; CancelTransferSuggestions(); RestoreTransferTypedName();
        try
        {
            var dialog = TransferConfirmationDialog; dialog.XamlRoot = WorkspaceRoot.XamlRoot; dialog.Title = title; dialog.Content = message;
            dialog.PrimaryButtonText = primary; dialog.SecondaryButtonText = secondary; dialog.CloseButtonText = close; dialog.DefaultButton = ContentDialogButton.Close;
            return await dialog.ShowAsync();
        }
        finally { transferDialogOpen = false; TransferDate.SuppressCompletion = TransferTime.SuppressCompletion = false; RenderTransferEditor(); }
    }
    private async void SaveTransfer(object sender, RoutedEventArgs args) => await SaveTransferAsync(ReferenceEquals(sender, TransferReuse));
    private async Task<bool> SaveTransferAsync(bool reuse, bool returnToList = true)
    {
        if (transferEditor is not { } state || currentLedger is null || TransferFrozen || transferDialogOpen || transferPreparingSave) return false;
        // TextChangedは非同期。プレビュー表示を読み戻さず、利用者入力の通知を先に処理する。
        transferPreparingSave = true;
        try
        {
            var delivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => { delivered.TrySetResult(true); })) return false;
            await delivered.Task;
        }
        finally { transferPreparingSave = false; }
        if (closed || !ReferenceEquals(state, transferEditor) || currentLedger is null || TransferFrozen) return false;
        TransferDate.Complete(); TransferTime.Complete(); SyncTransferInput();
        TransferSavePreparation prepared;
        try { prepared = PrepareTransfer(reuse); }
        catch (TransferInputException error) { TransferNotice(error.Message); FocusTransferField(error.Field); return false; }
        var warnings = new List<string>();
        if (reuse) warnings.Add("現在の入力内容を別の振替として登録します。元の振替は変更しません。");
        else if (state.IsExisting) warnings.Add("この振替を更新しますか？");
        if (!reuse && state.HasSavedDateChanged == true) warnings.Add("保存済みの日付から取引日が変更されています。");
        if (state.IsMidnight == true) warnings.Add("時刻が00:00:00です。このまま保存しますか？");
        if (warnings.Count != 0 && await ConfirmTransferAsync(reuse ? "流用登録の確認" : state.IsExisting ? "更新の確認" : "時刻の確認", string.Join("\n", warnings), reuse ? "流用登録" : state.IsExisting ? "更新" : "登録") != ContentDialogResult.Primary) return false;
        if (!ReferenceEquals(state, transferEditor) || TransferFrozen || currentLedger is null) return false;
        transferPendingSnapshot = prepared.Snapshot;
        transferPending = prepared.Kind == TransferSaveKind.Update
            ? TransferWriteRequest.Update(currentLedger.LedgerId, prepared.TransferId!.Value, prepared.Revision!.Value, prepared.Snapshot)
            : TransferWriteRequest.Create(currentLedger.LedgerId, prepared.Snapshot);
        return await SendPendingTransferAsync(returnToList);
    }
    private async void DeleteTransfer(object sender, RoutedEventArgs args)
    {
        if (transferEditor is not { } state || currentLedger is null || TransferFrozen || transferDialogOpen || transferPreparingSave) return;
        if (await ConfirmTransferAsync("振替の削除", $"「{state.Draft.Name}」を削除しますか？", "削除") != ContentDialogResult.Primary || !ReferenceEquals(state, transferEditor) || TransferFrozen) return;
        var target = state.PrepareDelete(); transferPendingSnapshot = null;
        transferPending = TransferWriteRequest.Delete(currentLedger.LedgerId, target.TransferId, target.Revision);
        await SendPendingTransferAsync(true);
    }
    private async void RetryTransferWrite(object sender, RoutedEventArgs args) { if (TransferCanRetryWrite && !transferSending) await SendPendingTransferAsync(true); }
    private async Task<bool> SendPendingTransferAsync(bool returnToList)
    {
        if (transferPending is not { } request || transferSending || transferEditor is not { } editor || currentLedger?.LedgerId != request.LedgerId) return false;
        var wasUnknown = transferUnknown;
        transferSending = true; transferUnknown = false; transferRetryAfterLogin = false; TransferEditorNotice.IsOpen = false; CancelTransferSuggestions(); transferAutofillCancellation?.Cancel(); RenderTransferEditor();
        var accountId = account?.AuthUserId; string? inputErrorField = null; var offerLatest = false;
        var workspace = new TransfersWorkspace(connection.Api, connection.Session, request.LedgerId);
        Guid? savedId = request.TransferId;
        try
        {
            if (request.Operation == TransferWriteOperation.Create) savedId = (await workspace.CreateAsync(request, lifetime.Token)).TransferId;
            else if (request.Operation == TransferWriteOperation.Update) await workspace.UpdateAsync(request, lifetime.Token);
            else await workspace.DeleteAsync(request, lifetime.Token);
        }
        catch (Exception error) when (error is AuthenticationRejectedException or AuthenticationContextChangedException)
        {
            transferUnknown = wasUnknown;
            NavigateToLogin("ログインを継続できません。入力を保持しています。もう一度ログインしてください。"); return false;
        }
        catch (TransferRequestException error)
        {
            transferPending = null; transferPendingSnapshot = null;
            if (error.StatusCode == HttpStatusCode.Forbidden)
            { editor.EnterReadOnly(TransferReadOnlyReason.Forbidden); TransferNotice("編集権限がなくなったため、保存できません"); }
            else if (error.StatusCode == HttpStatusCode.NotFound)
            { editor.EnterReadOnly(TransferReadOnlyReason.NotFound); TransferNotice("振替が削除されたか、アクセスできなくなりました。"); }
            else if (error.StatusCode == HttpStatusCode.Conflict)
            {
                if (request.Operation is TransferWriteOperation.Update or TransferWriteOperation.Delete && error.Code == "revision_conflict" && error.LatestTransfer is { } latest)
                {
                    editor.RegisterConflict(latest); offerLatest = true;
                    TransferNotice("他の端末で更新されています。入力を保持して読み取り専用にしました。編集を再開するには最新情報を読み込んでください。", InfoBarSeverity.Warning);
                }
                else { transferStopped = true; TransferNotice("要求を確認できません。入力を保持して書込を停止しました。"); }
            }
            else
            {
                TransferNotice(error.StatusCode == HttpStatusCode.TooManyRequests ? "操作回数の上限に達しました。しばらく待ってから保存してください。" : error.StatusCode == HttpStatusCode.ServiceUnavailable ? "保存先を利用できません。入力を保持しています。" : "入力内容を確認してください。");
                inputErrorField = error.Field;
            }
            return false;
        }
        catch (Exception error) when (error is TransferWriteUncertainException or ApiConnectionException or HttpRequestException or IOException or OperationCanceledException)
        { transferUnknown = true; TransferNotice("保存結果を確認できませんでした。同じ要求で再試行してください。"); return false; }
        finally
        {
            transferSending = false;
            if (ReferenceEquals(editor, transferEditor))
            {
                RenderTransferEditor();
                if (!TransferFrozen && inputErrorField is not null) FocusTransferField(inputErrorField);
                if (offerLatest && !closed) await OfferTransferConflictAsync(editor);
            }
        }
        if (!ReferenceEquals(editor, transferEditor) || closed) return false;
        var snapshot = transferPendingSnapshot; var deleted = request.Operation == TransferWriteOperation.Delete;
        transferPending = null; transferPendingSnapshot = null;
        // 保存後の残高キャッシュ連動は次工程。保存済み基準残高を変更しない。
        if (!deleted && snapshot is not null && recordSearch is not null && TransferMethods is { } methods)
        {
            recordSearch.ApplySavedTransfer(snapshot, methods); RenderRecordFilterControls();
        }
        ResetTransferSuggestions(); ResetTransferEditor();
        if (returnToList) ShowPage(HomePage);
        if (transferState is { } list && recordSearch is not null)
        {
            transferRestoringSaved = true;
            try
            {
                var receipt = receiptState?.StartAsync(receiptSearch!.Query(), lifetime.Token) ?? Task.CompletedTask;
                transferSavedSelection = deleted ? null : savedId;
                if (deleted) await list.StartAsync(recordSearch.TransferQuery(), lifetime.Token);
                else if (savedId is Guid id) await list.ShowSavedAsync(recordSearch.TransferQuery(), id, lifetime.Token);
                await receipt;
                if (!deleted) SelectSavedTransferRow();
            }
            finally { transferRestoringSaved = false; }
        }
        return !closed && account?.AuthUserId == accountId && currentLedger?.LedgerId == request.LedgerId;
    }
    private void RenderRecordFilterControls()
    {
        if (receiptSearch is null || recordSearch is null) return;
        receiptFiltersUpdating = true; ReceiptMonth.Text = receiptSearch.MonthText; TransferNameSearch.Text = recordSearch.TransferText;
        RecordMonthPeriod.IsChecked = !receiptSearch.AllPeriods; ReceiptAllPeriods.IsChecked = receiptSearch.AllPeriods;
        receiptFiltersUpdating = false; RefreshReceiptChoices(); RenderReceiptState();
    }
    private async Task OfferTransferConflictAsync(TransferEditorState editor)
    {
        if (!ReferenceEquals(editor, transferEditor) || !TransferCanAdoptLatest || transferDialogOpen) return;
        var choice = await ConfirmTransferAsync("振替が更新されています", "他の端末で更新されています。現在の入力は保持しています。", "最新を読み込む", close: "編集画面に戻る");
        if (choice == ContentDialogResult.Primary) await AdoptLatestTransferAsync(editor);
    }
    private async void LoadLatestTransfer(object sender, RoutedEventArgs args)
    { if (transferEditor is { } editor) await AdoptLatestTransferAsync(editor); }
    private async Task AdoptLatestTransferAsync(TransferEditorState editor)
    {
        if (!ReferenceEquals(editor, transferEditor) || !TransferCanAdoptLatest || transferDialogOpen || transferSending || currentLedger is null) return;
        var ledgerId = currentLedger.LedgerId; var accountId = account?.AuthUserId;
        var choice = await ConfirmTransferAsync("現在の入力を破棄しますか？", "現在の入力内容を破棄して、サーバーの最新情報を読み込みますか？", "最新を読み込む");
        if (choice != ContentDialogResult.Primary || closed || !ReferenceEquals(editor, transferEditor) || !TransferCanAdoptLatest || currentLedger?.LedgerId != ledgerId || account?.AuthUserId != accountId) return;
        // 応答内の検証済みlatestだけを採用し、詳細GETや保存を行わない。
        editor.AdoptLatest(true);
        if (TransferMethods is { } methods && editor.Draft.From.PaymentMethodId is Guid from && editor.Draft.To.PaymentMethodId is Guid to &&
            (!TransferEditorRules.CanUse(methods, from) || !TransferEditorRules.CanUse(methods, to)))
            editor.EnterReadOnly(TransferReadOnlyReason.Forbidden);
        PopulateTransferEditor();
    }
    private void SelectSavedTransferRow()
    {
        if (transferState?.SelectedTransferId is not Guid id) return;
        var row = transferRows.FirstOrDefault(r => r.Transfer.TransferId == id);
        if (row is not null) { TransferList.SelectedItem = row; TransferList.ScrollIntoView(row); transferSavedSelection = null; }
    }
    private void FocusTransferField(string field)
    {
        Control target = field switch { "transactionAt" => TransferDate, "name" => TransferName, "fromPaymentMethodId" => TransferFrom, "toPaymentMethodId" => TransferTo, "amount" => TransferAmount, "memo" => TransferMemo, _ => TransferName };
        target.Focus(FocusState.Programmatic);
    }
    private async Task<bool> LeaveTransferEditorAsync()
    {
        if (transferSending || transferDialogOpen || transferLeaving || transferPreparingSave) return false;
        if (transferEditor is not { } state) { CancelTransferOpening(); return true; }
        SyncTransferInput(); transferLeaving = true;
        try
        {
            if (transferPending is not null && transferUnknown)
            {
                if (await ConfirmTransferAsync("結果未確認の操作があります", "保存結果を確認できていません。離れてもサーバー側の処理は取り消されません。", "結果未確認のまま離れる", close: "結果の確認に戻る") != ContentDialogResult.Primary) return false;
            }
            else if (state.NeedsLeaveConfirmation)
            {
                var writable = !TransferFrozen;
                var answer = await ConfirmTransferAsync("未保存の内容があります", writable ? "入力内容を保存して移動しますか？" : "入力内容を破棄して閉じますか？", writable ? "保存して移動" : "破棄して閉じる", writable ? "破棄して移動" : "", "編集に戻る");
                if (answer == ContentDialogResult.None) return false;
                if (writable && answer == ContentDialogResult.Primary) return await SaveTransferAsync(false, returnToList: false);
            }
            ResetTransferEditor(); transferRestorePending = true; receiptRestorePending = true; return true;
        }
        finally { transferLeaving = false; }
    }
}
