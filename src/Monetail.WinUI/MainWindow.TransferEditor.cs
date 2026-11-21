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
    private TransferEditorState? transferEditor;
    private CancellationTokenSource? transferOpening;
    private Guid? transferOpenRetry;
    private bool transferDialogOpen, transferLeaving, transferCloseApproved;
    private bool RecordEditorActive => receiptEditor is not null || transferEditor is not null;
    private PaymentMethodsInfo? TransferMethods => currentLedger is { } ledger ? connection.Masters.Payments(ledger.LedgerId) : null;
    private PeopleInfo? TransferPeople => currentLedger is { } ledger ? connection.Masters.People(ledger.LedgerId) : null;
    private bool TransferRecoveryBlocked => transferAwaitingLogin || transferMastersLoading || transferMastersFailed;
    private bool TransferFrozen => TransferEntry.ViewModel?.IsFrozen != false;
    private bool TransferCanAdoptLatest => TransferEntry.ViewModel?.CanAdoptLatest == true;
    private bool TransferCanRetryWrite => TransferEntry.ViewModel?.CanRetryPendingWrite == true;
    private void InitializeTransferEditor()
    {
        WorkspaceRoot.Children.Remove(TransferConfirmationDialog);
        TransferEntry.IsCancellationTarget = IsTransferLeaveTarget;
        AppWindow.Closing += async (_, args) =>
        {
            if (transferAwaitingLogin)
            {
                DiscardTransferAuthentication();
                return;
            }
            if (transferCloseApproved || transferEditor is null)
                return;
            args.Cancel = true;
            if (await LeaveTransferEditorAsync())
            {
                transferCloseApproved = true;
                Close();
            }
        };
    }
    private bool IsTransferLeaveTarget(DependencyObject? element) => ReferenceEquals(element, SettingsButton) ||
        ReferenceEquals(element, RecordButton) || ReferenceEquals(element, LedgerSelector);
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
        if (recordHome is not null) recordHome.TransferOpening = new("振替を読み込み中…", true, false);
        UpdateTransferActions();
        bool Current() => !closed && ReferenceEquals(transferOpening, pending) && !pending.IsCancellationRequested &&
            currentLedger?.LedgerId == ledger && account?.AuthUserId == person &&
            IsRecordPageVisible && !RecordEditorActive;
        try
        {
            var saved = id is Guid value ? await new TransfersWorkspace(api, session, ledger).GetAsync(value, pending.Token) : null;
            await connection.Masters.GetPeopleAsync(ledger, ct => new PeopleWorkspace(api, session, ledger).ListAsync(ct), pending.Token);
            await connection.Masters.GetPaymentsAsync(ledger, ct => new PaymentMethodsWorkspace(api, session, ledger).ListAsync(ct), pending.Token);
            if (!Current() || TransferMethods is not { } methods)
                return;
            if (saved is null && !TransferEditorRules.CanCreate(methods))
            {
                if (recordHome is not null) recordHome.TransferOpening = new("振替を登録する権限がありません。", false, false);
                return;
            }
            transferEditor = saved is null ? TransferEditorState.Create(DateOnly.FromDateTime(DateTime.Now)) : TransferEditorState.Edit(saved);
            if (saved is not null && (!TransferEditorRules.CanUse(methods, saved.FromPaymentMethodId) || !TransferEditorRules.CanUse(methods, saved.ToPaymentMethodId)))
                transferEditor.EnterReadOnly(TransferReadOnlyReason.Forbidden);
            var model = new TransferEditorViewModel(transferEditor, () => new(TransferRecoveryBlocked, transferMastersFailed, transferMastersLoading, transferDialogOpen, account is not null),
                paymentId => connection.Masters.PaymentName(ledger, paymentId), LoadTransferSuggestionsAsync, LoadTransferAutofillAsync);
            model.ActionRequested += ExecuteTransferActionAsync;
            model.AuthenticationRequired += StopTransferAuthentication;
            TransferEntry.ViewModel = model;
            RecordPage?.RememberPositions();
            RecordPage?.ShowEditor(EditorContents);
            TransferEditorPage.Visibility = Visibility.Visible;
            PopulateTransferEditor();
            TransferEntry.FocusField("transactionAt");
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
                if (recordHome is not null && transferEditor is not null) recordHome.TransferOpening = null;
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
        transferAuthentication = null;
        transferAwaitingLogin = transferMastersLoading = transferMastersFailed = transferRecoveryLedgerReady = false;
        CancelTransferOpening();
        var transferModel = TransferEntry.ViewModel;
        TransferEntry.ViewModel = null;
        transferModel?.DiscardWrite();
        TransferEntry.Discard();
        transferEditor = null;
        TransferEditorPage.Visibility = Visibility.Collapsed;
        RecordPage?.ShowEditor(null);
    }
    private void PopulateTransferEditor()
    {
        TransferEntry.ViewModel?.UpdateMasters(TransferPeople, TransferMethods);
        TransferEntry.Populate();
    }
    private void SyncTransferInput() => TransferEntry.SynchronizeInput();
    private void RefreshTransferEditorChoices()
    {
        TransferEntry.ViewModel?.UpdateMasters(TransferPeople, TransferMethods);
        TransferEntry.RefreshChoices();
    }
    private TransferSavePreparation PrepareTransfer(bool reuse) => TransferEntry.ViewModel?.PrepareSave(reuse)
        ?? throw new TransferInputException("master", "マスタ情報を取得してください。");
    private void RenderTransferEditor() => TransferEntry.ViewModel?.UpdateMasters(TransferPeople, TransferMethods);
    private void TransferNotice(string message, InfoBarSeverity severity = InfoBarSeverity.Error) =>
        TransferEntry.ViewModel?.ShowNotice(message, severity switch
        {
            InfoBarSeverity.Informational => TransferNoticeLevel.Information,
            InfoBarSeverity.Warning => TransferNoticeLevel.Warning,
            _ => TransferNoticeLevel.Error
        });
    private Task ExecuteTransferActionAsync(TransferEditorAction action) => action switch
    {
        TransferEditorAction.Back => BackTransferAsync(),
        TransferEditorAction.Save => SaveTransferAsync(false),
        TransferEditorAction.Reuse => SaveTransferAsync(true),
        TransferEditorAction.Delete => DeleteTransferAsync(),
        TransferEditorAction.RetryWrite => RetryTransferWriteAsync(),
        TransferEditorAction.RetryMasters => RetryTransferMastersAsync(),
        TransferEditorAction.LoadLatest => LoadLatestTransferAsync(),
        _ => Task.CompletedTask
    };
    private async Task BackTransferAsync()
    {
        if (await LeaveTransferEditorAsync())
        {
            NavigateWorkspace(WorkspaceScreen.HomePage);
            RestoreTransferPosition();
        }
    }
    private void RestoreTransferPosition() => RecordPage?.RestorePositions();
}
