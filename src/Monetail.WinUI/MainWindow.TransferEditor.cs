using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transfers;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Presentation.Transfers;

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
    private TransferEditorState? transferEditor => TransferPage?.ViewModel?.Editor;
    private CancellationTokenSource? transferOpening;
    private Guid? transferOpenRetry;
    private bool RecordEditorActive => receiptEditor is not null || transferEditor is not null;
    private PaymentMethodsInfo? TransferMethods => currentLedger is { } ledger ? connection.Masters.Payments(ledger.LedgerId) : null;
    private PeopleInfo? TransferPeople => currentLedger is { } ledger ? connection.Masters.People(ledger.LedgerId) : null;
    private async Task<bool> LeaveRecordEditorAsync() => await LeaveTransferEditorAsync() && await LeaveReceiptEditorAsync();
    internal async Task OpenTransferEditorAsync(Guid? id)
    {
        if (busy || closed || ManagementDialogOpen || transferOpening is not null || receiptOpening is not null ||
            RecordEditorActive || account is null || currentLedger is null || !IsRecordPageVisible)
            return;
        var ledger = currentLedger.LedgerId;
        var person = account.AuthUserId;
        var api = connection.Api;
        var session = connection.Session;
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
                if (recordHome is not null && transferEditor is not null)
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
        if (recordHome is not null) recordHome.TransferOpening = null;
        UpdateTransferActions();
    }
    private void ResetTransferEditor()
    {
        CancelTransferOpening();
        TransferPage?.Release();
    }
    private void RefreshTransferEditorChoices()
    {
        if (TransferPage is not { } page)
            return;
        page.ViewModel?.UpdateMasters(TransferPeople, TransferMethods);
        page.RefreshChoices();
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
}
