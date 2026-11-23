using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Client.Transfers;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Presentation.Receipts;
using Monetail.Windows.Presentation.Records;
using Monetail.Windows.Presentation.Transfers;
using System.Net;

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
    private bool closeApproved;
    private async void ConfirmWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (closeApproved || EditorAwaitingLogin || !RecordEditorActive)
            return;
        args.Cancel = true;
        if (await LeaveRecordEditorAsync())
        {
            closeApproved = true;
            Close();
        }
    }
    private bool IsEditorLeaveTarget(DependencyObject? element) => ReferenceEquals(element, SettingsButton) ||
        ReferenceEquals(element, RecordButton) || ReferenceEquals(element, LedgerSelector);
    private RecordEditorContext CreateRecordEditorContext() => new(connection.Api, connection.Session, connection.Masters,
        currentLedger!.LedgerId, account!.AuthUserId, connection.Endpoint!.Value, lifetime.Token);
    private bool EditorContextMatches(RecordEditorContext context) => account?.AuthUserId == context.AccountId &&
        currentLedger?.LedgerId == context.LedgerId && connection.Endpoint?.Value == context.Origin;
    private void NavigateReceiptEditor(ReceiptEditorViewModel model)
    {
        RecordPage?.RememberPositions();
        if (recordHome is not null)
            recordHome.ReceiptOpening = null;
        NavigateWorkspace(WorkspaceScreen.ReceiptEditor, model);
    }
    private void ReturnFromReleasedEditor()
    {
        if (!RecordEditorActive && (ReceiptPage is not null || TransferPage is not null))
            NavigateWorkspace(WorkspaceScreen.HomePage);
    }
    private void ShowRetainedEditor()
    {
        ReleaseAuthentication();
        ReleaseLedgerPage();
        WorkspaceHost.Visibility = WorkspaceHeader.Visibility = Visibility.Visible;
    }
    private Guid? receiptOpenRetryId;
    private CancellationTokenSource? receiptOpening;


    // 一要求だけを受理。取消後に通信が完了しても同一要求・本人・家計簿・画面を照合して適用しない。
    internal async Task OpenReceiptEditorAsync(Guid? receiptId)
    {
        if (busy || closed || ManagementDialogOpen || receiptOpening is not null || transferOpening is not null || RecordEditorActive || account is null || currentLedger is null || !IsRecordPageVisible)
            return;
        var ledger = currentLedger.LedgerId;
        var person = account.AuthUserId;
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        receiptOpening = pending;
        receiptOpenRetryId = receiptId;

        UpdateReceiptActions();
        if (recordHome is not null)
            recordHome.ReceiptOpening = new("レシートを読み込み中…", true, false);
        bool Current() => !closed && ReferenceEquals(receiptOpening, pending) && !pending.IsCancellationRequested &&
            currentLedger?.LedgerId == ledger && account?.AuthUserId == person && IsRecordPageVisible;
        try
        {
            var model = await ReceiptEditorViewModel.OpenAsync(CreateRecordEditorContext(), receiptId, pending.Token);
            if (!Current())
            {
                model.Discard();
                return;
            }
            model.ActionRequested += action => action switch
            {
                ReceiptEditorAction.Back => BackFromReceiptEditorAsync(),
                ReceiptEditorAction.RetryMasters => RetryReceiptMastersAsync(),
                _ => ReportReceiptCategoryErrorAsync()
            };
            model.AuthenticationRequired += StopReceiptAuthentication;
            NavigateReceiptEditor(model);
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested) { }
        catch (Exception error) when (error is AuthenticationRejectedException or AuthenticationContextChangedException)
        {
            if (Current())
                NavigateToLogin("ログインを継続できません。もう一度ログインしてください。");
        }
        catch (Exception error) when (error is TaxRateRequestException or ReceiptRequestException or CategoryRequestException or PeopleRequestException or PaymentMethodRequestException or ApiConnectionException or HttpRequestException or OperationCanceledException)
        {
            if (Current())
            {
                if (recordHome is not null)
                    recordHome.ReceiptOpening = new(error is ReceiptRequestException { StatusCode: HttpStatusCode.NotFound }
                ? "このレシートは開けません。参加状態と対象を確認してください。"
                : "レシートまたは税率履歴を取得できませんでした。再試行してください。", false, true);
            }
        }
        finally
        {
            if (ReferenceEquals(receiptOpening, pending))
            {
                receiptOpening = null;
                if (recordHome is not null && ReceiptPage?.ViewModel is not null)
                    recordHome.ReceiptOpening = null;
                UpdateReceiptActions();
            }
        }
    }
    private void CancelReceiptOpening()
    {
        var pending = receiptOpening;
        receiptOpening = null;
        pending?.Cancel();
        if (recordHome is not null)
            recordHome.ReceiptOpening = null;
        UpdateReceiptActions();
    }
    private void ResetReceiptEditor()
    {
        CancelReceiptOpening();
        ReceiptPage?.Release();
    }
    private Task ReportReceiptCategoryErrorAsync()
    {
        ShowError("科目情報を確認できないため切り替えできません。入力は保持しています。");
        return Task.CompletedTask;
    }
    private void StopReceiptAuthentication() => NavigateToLogin("ログインを継続できません。入力を保持しています。もう一度ログインしてください。");
    private async Task BackFromReceiptEditorAsync()
    {
        if (await LeaveReceiptEditorAsync())
            NavigateWorkspace(WorkspaceScreen.HomePage);
    }
    private async Task<bool> LeaveReceiptEditorAsync()
    {
        if (ReceiptPage is { } page)
        {
            if (!await page.TryLeaveAsync())
                return false;
            page.Release();
            return true;
        }
        CancelReceiptOpening();
        return true;
    }
    private bool SuspendReceiptForAuthentication()
    {
        if (ReceiptPage is not { ViewModel.Context: { } context } page || context.Origin != connection.Endpoint?.Value)
            return false;
        page.SuspendForAuthentication();
        recordHome?.Suspend();
        return true;
    }
    private void RememberReceiptAuthenticationView() => ReceiptPage?.RememberPosition();
    private async Task<bool> ResumeReceiptEditAsync(CancellationToken token)
    {
        if (ReceiptPage is not { ViewModel: { AwaitingLogin: true, Context: { } context } } page || account is null)
            return false;
        if (!EditorContextMatches(context))
        {
            DiscardLedgerContents();
            ClearLedger();
            return false;
        }
        context.Rebind(connection.Api, connection.Session);
        ShowRetainedEditor();
        await ReloadReceiptMastersAsync(page, token);
        return true;
    }
    private async Task RetryReceiptMastersAsync()
    {
        if (ReceiptPage is { ViewModel.ShowRetryMasters: true } page)
            await ReloadReceiptMastersAsync(page, lifetime.Token);
    }
    private async Task ReloadReceiptMastersAsync(Monetail.WinUI.Views.Receipts.ReceiptEditorPage page, CancellationToken token)
    {
        var ledger = await page.ResumeAsync(token);
        if (closed || !ReferenceEquals(ReceiptPage, page) || page.ViewModel?.AwaitingLogin == true)
            return;
        if (ledger is not null)
        {
            DisplayLedgerHeader(ledger);
        }
    }

    private CancellationTokenSource? transferOpening;
    private Guid? transferOpenRetry;
    private bool RecordEditorActive => ReceiptPage?.ViewModel is not null || TransferPage?.ViewModel is not null;
    private PaymentMethodsInfo? TransferMethods => currentLedger is { } ledger ? connection.Masters.Payments(ledger.LedgerId) : null;
    private async Task<bool> LeaveRecordEditorAsync() => await LeaveTransferEditorAsync() && await LeaveReceiptEditorAsync();
    internal async Task OpenTransferEditorAsync(Guid? id)
    {
        if (busy || closed || ManagementDialogOpen || transferOpening is not null || receiptOpening is not null ||
            RecordEditorActive || account is null || currentLedger is null || !IsRecordPageVisible)
            return;
        var ledger = currentLedger.LedgerId;
        var person = account.AuthUserId;
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        transferOpening = pending;
        transferOpenRetry = id;
        if (recordHome is not null)
            recordHome.TransferOpening = new("振替を読み込み中…", true, false);
        UpdateTransferActions();
        bool Current() => !closed && ReferenceEquals(transferOpening, pending) && !pending.IsCancellationRequested &&
            currentLedger?.LedgerId == ledger && account?.AuthUserId == person &&
            IsRecordPageVisible && !RecordEditorActive;
        try
        {
            var model = await TransferEditorViewModel.OpenAsync(CreateRecordEditorContext(), id, pending.Token);
            if (!Current())
            {
                model?.Release();
                return;
            }
            if (model is null)
            {
                if (recordHome is not null)
                    recordHome.TransferOpening = new("振替を登録する権限がありません。", false, false);
                return;
            }
            model.AuthenticationRequired += StopTransferAuthentication;
            RecordPage?.RememberPositions();
            if (recordHome is not null)
                recordHome.TransferOpening = null;
            NavigateWorkspace(WorkspaceScreen.TransferEditor, model);
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested)
        {
        }
        catch (Exception error) when (error is AuthenticationRejectedException or AuthenticationContextChangedException)
        {
            if (Current())
                NavigateToLogin("ログインを継続できません。もう一度ログインしてください。");
        }
        catch (Exception error) when (error is TransferRequestException or PeopleRequestException or
            PaymentMethodRequestException or ApiConnectionException or HttpRequestException or OperationCanceledException)
        {
            if (Current())
            {
                if (recordHome is not null)
                    recordHome.TransferOpening = new(error is TransferRequestException { StatusCode: HttpStatusCode.NotFound }
                        ? "振替が削除されたか、アクセスできなくなりました。" : "振替またはマスタ情報を取得できませんでした。", false, true);

            }
        }
        finally
        {
            if (ReferenceEquals(transferOpening, pending))
            {
                transferOpening = null;
                if (recordHome is not null && TransferPage?.ViewModel is not null)
                    recordHome.TransferOpening = null;
                UpdateTransferActions();
            }
        }
    }
    private void CancelTransferOpening()
    {
        var pending = transferOpening;
        transferOpening = null;
        pending?.Cancel();
        if (recordHome is not null)
            recordHome.TransferOpening = null;
        UpdateTransferActions();
    }
    private void ResetTransferEditor()
    {
        CancelTransferOpening();
        TransferPage?.Release();
    }
    private async Task BackTransferAsync()
    {
        if (await LeaveTransferEditorAsync())
            NavigateWorkspace(WorkspaceScreen.HomePage);
    }
    private async Task<bool> LeaveTransferEditorAsync()
    {
        if (TransferPage is { } page)
        {
            if (!await page.TryLeaveAsync())
                return false;
            page.Release();
            return true;
        }
        CancelTransferOpening();
        return true;
    }
    private void StopTransferAuthentication() => NavigateToLogin("ログインを継続できません。入力を保持しています。もう一度ログインしてください。");
    private bool SuspendTransferForAuthentication()
    {
        if (TransferPage is not { ViewModel.Context: { } context } page || context.Origin != connection.Endpoint?.Value)
            return false;
        page.SuspendForAuthentication();
        recordHome?.Suspend();
        return true;
    }
    private async Task<bool> ResumeTransferEditAsync(CancellationToken token)
    {
        if (TransferPage is not { ViewModel: { AwaitingLogin: true, Context: { } context } } page || account is null)
            return false;
        if (!EditorContextMatches(context))
        {
            DiscardLedgerContents();
            ClearLedger();
            return false;
        }
        context.Rebind(connection.Api, connection.Session);
        ShowRetainedEditor();
        await ReloadTransferMastersAsync(page, token);
        return true;
    }
    private async Task RetryTransferMastersAsync()
    {
        if (TransferPage is { ViewModel.MastersFailed: true } page)
            await ReloadTransferMastersAsync(page, lifetime.Token);
    }
    private async Task ReloadTransferMastersAsync(Monetail.WinUI.Views.Transfers.TransferEditorPage page, CancellationToken token)
    {
        var ledger = await page.ResumeAsync(token);
        if (closed || !ReferenceEquals(TransferPage, page) || page.ViewModel?.AwaitingLogin == true)
            return;
        if (ledger is not null)
        {
            DisplayLedgerHeader(ledger);
            management?.Context.ApplyPermissions(new(ledger.Role, connection.Masters.MyPersonId(ledger.LedgerId)));
        }
    }

    private async Task<bool> TransferSavedAsync(TransferWriteCompletion completion)
    {
        var request = completion.Request;
        var snapshot = completion.Snapshot;
        var deleted = request.Operation == TransferWriteOperation.Delete;
        if (closed || TransferPage?.ViewModel?.Context is not { } context || !EditorContextMatches(context))
            return false;
        // 一覧条件の調整と保存行探索だけをShellで調停する。残高連動は未実装。
        if (!deleted && snapshot is not null && recordSearch is not null && TransferMethods is { } methods)
        {
            recordSearch.ApplySavedTransfer(snapshot, methods);
            recordSearch.ApplySharedText(recordSearch.TransferText);
            RefreshReceiptChoices();
            recordHome?.RefreshSearch();
        }
        ResetTransferEditor();
        // 従来の入力領域解除と同様に一覧を表示してから再取得し、401の通常処理へ接続する。
        ReturnFromReleasedEditor();
        if (TransferListModel is { } list && recordSearch is not null)
        {
            var query = recordSearch.TransferQuery();
            var receipt = ReceiptListModel?.StartAsync(receiptSearch!.Query(), lifetime.Token) ?? Task.CompletedTask;
            if (deleted)
                await list.StartAsync(query, lifetime.Token);
            else if (completion.SavedId is Guid id)
                await list.ShowSavedAsync(query, id, lifetime.Token);
            await receipt;
        }
        // 一覧再取得中の認証喪失・終了後は、保存して移動の後続遷移を許可しない。
        return !closed && EditorContextMatches(context);
    }
}
