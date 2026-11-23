using System.ComponentModel;
using Microsoft.UI.Xaml;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Client.Transport;
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

    private CancellationTokenSource? recordMasterLifetime;
    internal bool ReceiptHomeReady => account is null || (ReceiptListModel is { HasLoaded: true } or { Failure: not null }) && TransferHomeReady;
    internal bool TransferHomeReady => account is null || TransferListModel is { HasLoaded: true } or { Failure: not null };

    private void InitializeReceipts() => connection.Masters.Changed += ReceiptMasterChanged;
    private void EnsureRecordHome()
    {
        if (recordHome is not null || account is null || currentLedger is null)
            return;
        var ledger = currentLedger.LedgerId;
        var api = connection.Api;
        var session = connection.Session;
        var search = new RecordSearchState(DateOnly.FromDateTime(DateTime.Now));
        recordMasterLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var masterToken = recordMasterLifetime.Token;
        var receipts = new ReceiptListViewModel(async (query, token) =>
        {
            await GetRecordMastersAsync(ledger, api, session, masterToken, token);
            return await new ReceiptsWorkspace(api, session, ledger).ListAsync(query, token);
        }, search, id => connection.Masters.PaymentName(ledger, id));
        var transfers = new TransferListViewModel(async (query, token) =>
        {
            await GetRecordMastersAsync(ledger, api, session, masterToken, token);
            return await new TransfersWorkspace(api, session, ledger).ListAsync(query, token);
        }, search, id => connection.Masters.PaymentName(ledger, id));
        recordHome = new(search, receipts, transfers);
        receipts.OpenRequested += OpenReceiptEditorAsync;
        transfers.OpenRequested += OpenTransferEditorAsync;
        receipts.PropertyChanged += ReceiptListChanged;
        transfers.PropertyChanged += ReceiptListChanged;
        recordHome.RetryReceiptRequested += RetryReceiptOpeningAsync;
        recordHome.RetryTransferRequested += RetryTransferOpeningAsync;
        UpdateReceiptActions();
        UpdateTransferActions();
        RefreshReceiptChoices();
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
        recordMasterLifetime?.Cancel();
        recordMasterLifetime?.Dispose();
        recordMasterLifetime = null;
        connection.Masters.Clear();
    }

    private async Task GetRecordMastersAsync(Guid ledger, MonetailApiClient api, ClientSession session, CancellationToken masterToken, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await connection.Masters.GetPeopleAsync(ledger, ct => new PeopleWorkspace(api, session, ledger).ListAsync(ct), masterToken).WaitAsync(token);
        token.ThrowIfCancellationRequested();
        await connection.Masters.GetPaymentsAsync(ledger, ct => new PaymentMethodsWorkspace(api, session, ledger).ListAsync(ct), masterToken).WaitAsync(token);
    }

    private void ReceiptMasterChanged(Guid ledger)
    {
        if (currentLedger?.LedgerId != ledger || closed)
            return;
        ReceiptPage?.MastersChanged();
        TransferPage?.MastersChanged();
        RefreshReceiptChoices();
        ReceiptListModel?.RefreshNames();
        TransferListModel?.RefreshNames();
        UpdateTransferActions();
    }

    private void RefreshReceiptChoices()
    {
        if (currentLedger is { } ledger)
            recordHome?.RefreshChoices(connection.Masters.People(ledger.LedgerId), connection.Masters.Payments(ledger.LedgerId), connection.Masters.MyPersonId(ledger.LedgerId));
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
