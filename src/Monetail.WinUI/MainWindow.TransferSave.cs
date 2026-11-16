using System.Net;
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
        transferDialogOpen = true; TransferDate.SuppressCompletion = TransferTime.SuppressCompletion = true; CancelTransferSuggestions();
        try
        {
            var dialog = TransferConfirmationDialog; dialog.XamlRoot = WorkspaceRoot.XamlRoot; dialog.Title = title; dialog.Content = message;
            dialog.PrimaryButtonText = primary; dialog.SecondaryButtonText = secondary; dialog.CloseButtonText = close; dialog.DefaultButton = ContentDialogButton.Close;
            return await dialog.ShowAsync();
        }
        finally { transferDialogOpen = false; TransferDate.SuppressCompletion = TransferTime.SuppressCompletion = false; }
    }
    private async void SaveTransfer(object sender, RoutedEventArgs args) => await SaveTransferAsync(ReferenceEquals(sender, TransferReuse));
    private async Task<bool> SaveTransferAsync(bool reuse, bool returnToList = true)
    {
        if (transferEditor is not { } state || currentLedger is null || TransferFrozen || transferDialogOpen) return false;
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
        if (transferEditor is not { } state || currentLedger is null || TransferFrozen || transferDialogOpen) return;
        if (await ConfirmTransferAsync("振替の削除", $"「{state.Draft.Name}」を削除しますか？", "削除") != ContentDialogResult.Primary || !ReferenceEquals(state, transferEditor) || TransferFrozen) return;
        var target = state.PrepareDelete(); transferPendingSnapshot = null;
        transferPending = TransferWriteRequest.Delete(currentLedger.LedgerId, target.TransferId, target.Revision);
        await SendPendingTransferAsync(true);
    }
    private async void RetryTransferWrite(object sender, RoutedEventArgs args) { if (transferUnknown && !transferSending) await SendPendingTransferAsync(true); }
    private async Task<bool> SendPendingTransferAsync(bool returnToList)
    {
        if (transferPending is not { } request || transferSending || transferEditor is not { } editor || currentLedger?.LedgerId != request.LedgerId) return false;
        var wasUnknown = transferUnknown;
        transferSending = true; transferUnknown = false; TransferEditorNotice.IsOpen = false; CancelTransferSuggestions(); transferAutofillCancellation?.Cancel(); RenderTransferEditor();
        var accountId = account?.AuthUserId; string? inputErrorField = null;
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
            // 同一本人への編集復元は次工程。現段階では入力と元要求を保持して安全に停止する。
            transferUnknown = wasUnknown; transferStopped = true; TransferNotice("ログインを継続できません。入力を保持していますが、再認証後の編集復元はまだ利用できません。"); return false;
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
                if (error.Code == "revision_conflict" && error.LatestTransfer is { } latest) editor.RegisterConflict(latest);
                transferStopped = true;
                TransferNotice(error.Code == "revision_conflict" ? "他の端末で更新されています。入力を保持して読み取り専用にしました。最新情報の採用は次工程で対応します。" : "要求を確認できません。入力を保持して書込を停止しました。");
            }
            else
            {
                TransferNotice(error.StatusCode == HttpStatusCode.TooManyRequests ? "操作回数の上限に達しました。しばらく待ってから保存してください。" : error.StatusCode == HttpStatusCode.ServiceUnavailable ? "保存先を利用できません。入力を保持しています。" : "入力内容を確認してください。");
                inputErrorField = error.Field;
            }
            return false;
        }
        catch (Exception error) when (error is ApiConnectionException or HttpRequestException or IOException or OperationCanceledException)
        { transferUnknown = true; TransferNotice("保存結果を確認できませんでした。同じ要求で再試行してください。"); return false; }
        finally { transferSending = false; if (ReferenceEquals(editor, transferEditor)) { RenderTransferEditor(); if (!TransferFrozen && inputErrorField is not null) FocusTransferField(inputErrorField); } }
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
        if (transferSending || transferDialogOpen || transferLeaving) return false;
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
                var answer = await ConfirmTransferAsync("未保存の内容があります", "入力内容を保存して移動しますか？", writable ? "保存して移動" : "破棄して閉じる", writable ? "破棄して移動" : "", "編集に戻る");
                if (answer == ContentDialogResult.None) return false;
                if (writable && answer == ContentDialogResult.Primary) return await SaveTransferAsync(false, returnToList: false);
            }
            ResetTransferEditor(); transferRestorePending = true; receiptRestorePending = true; return true;
        }
        finally { transferLeaving = false; }
    }
}
