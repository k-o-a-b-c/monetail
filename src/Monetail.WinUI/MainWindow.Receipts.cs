using System.ComponentModel;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Presentation.Receipts;
using Monetail.Windows.Presentation.Transfers;
using Monetail.Windows.Presentation.Workspace;

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
    private RecordHomeViewModel? recordHome;
    private ReceiptListViewModel? ReceiptListModel => recordHome?.Receipts;
    private TransferListViewModel? TransferListModel => recordHome?.Transfers;
    private RecordSearchState? recordSearch => recordHome?.Search;
    private ReceiptSearchState? receiptSearch => recordSearch?.Receipts;

    internal bool ReceiptHomeReady => account is null || (ReceiptListModel is { HasLoaded: true } or { Failure: not null }) && TransferHomeReady;
    internal bool TransferHomeReady => account is null || TransferListModel is { HasLoaded: true } or { Failure: not null };

    private void InitializeReceipts() => connection.Masters.Changed += ReceiptMasterChanged;
    private void EnsureRecordHome()
    {
        if (recordHome is not null || account is null || currentLedger is null)
            return;
        recordHome = new(currentLedger.LedgerId, DateOnly.FromDateTime(DateTime.Now), connection.Masters,
            connection.Api, connection.Session, lifetime.Token);
        recordHome.Receipts.OpenRequested += OpenReceiptEditorAsync;
        recordHome.Transfers.OpenRequested += OpenTransferEditorAsync;
        recordHome.Receipts.PropertyChanged += ReceiptListChanged;
        recordHome.Transfers.PropertyChanged += ReceiptListChanged;
        recordHome.RetryReceiptRequested += RetryReceiptOpeningAsync;
        recordHome.RetryTransferRequested += RetryTransferOpeningAsync;
        UpdateReceiptActions();
        UpdateTransferActions();
    }

    private Task RetryReceiptOpeningAsync() => OpenReceiptEditorAsync(receiptOpenRetryId);
    private Task RetryTransferOpeningAsync() => OpenTransferEditorAsync(transferOpenRetry);
    private void ResetRecordHome()
    {
        RecordPage?.Suspend();
        if (recordHome is { } model)
        {
            model.Receipts.OpenRequested -= OpenReceiptEditorAsync;
            model.Transfers.OpenRequested -= OpenTransferEditorAsync;
            model.Receipts.PropertyChanged -= ReceiptListChanged;
            model.Transfers.PropertyChanged -= ReceiptListChanged;
            model.RetryReceiptRequested -= RetryReceiptOpeningAsync;
            model.RetryTransferRequested -= RetryTransferOpeningAsync;
            model.Dispose();
            recordHome = null;
        }

        if (RecordPage is not null && !RecordEditorActive)
            ReleaseWorkspace();
        else
            RecordPage?.SetViewModel(null);
        connection.Masters.Clear();
    }

    private void ReceiptMasterChanged(Guid ledger)
    {
        if (currentLedger?.LedgerId != ledger || closed)
            return;
        ReceiptPage?.MastersChanged();
        TransferPage?.MastersChanged();
        recordHome?.MastersChanged(ledger);
        UpdateTransferActions();
    }

    private void ReceiptListChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (closed || (sender != ReceiptListModel && sender != TransferListModel) || args.PropertyName != "Failure")
            return;
        var failure = sender == ReceiptListModel ? ReceiptListModel?.Failure : TransferListModel?.Failure;
        if (!RecordEditorActive && IsRecordPageVisible && failure is (AuthenticationRejectedException or AuthenticationContextChangedException))
            NavigateToLogin("ログインを継続できません。もう一度ログインしてください。");
        UpdateTransferActions();
    }

    private void UpdateReceiptActions()
    {
        if (ReceiptListModel is { } model)
            model.CanRegister = account is not null && receiptOpening is null;
    }

    private void UpdateTransferActions()
    {
        if (TransferListModel is { } model)
            model.CanRegister = account is not null && transferOpening is null && TransferMethods is { } methods && Monetail.Windows.Client.Transfers.TransferEditorRules.CanCreate(methods);
    }
}
