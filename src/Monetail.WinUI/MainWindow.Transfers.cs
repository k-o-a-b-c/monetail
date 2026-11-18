using System.ComponentModel;
using Microsoft.UI.Xaml;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transfers;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Presentation.Transfers;

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
    private TransferListViewModel? TransferListModel => TransfersView.ViewModel;
    internal bool TransferHomeReady => account is null || TransferListModel is { HasLoaded: true } or { Failure: not null };

    private void ResetTransfers()
    {
        if (TransferListModel is { } model)
        {
            model.PropertyChanged -= TransferListChanged;
            model.OpenRequested -= OpenTransferEditorAsync;
            model.Dispose();
        }
        TransfersView.ViewModel = null;
        transferRestorePending = false;
    }
    private void StartTransferHome(Guid ledger, MonetailApiClient api, ClientSession session, CancellationToken masterToken)
    {
        var workspace = new TransfersWorkspace(api, session, ledger);
        var model = new TransferListViewModel(async (query, token) =>
        {
            await GetRecordMastersAsync(ledger, api, session, masterToken, token);
            return await workspace.ListAsync(query, token);
        }, recordSearch!, id => connection.Masters.PaymentName(ledger, id));
        model.OpenRequested += OpenTransferEditorAsync;
        model.PropertyChanged += TransferListChanged;
        TransfersView.ViewModel = model;
        UpdateTransferActions();
        _ = model.StartAsync(recordSearch!.TransferQuery(), lifetime.Token);
    }
    private void TransferListChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (closed || !ReferenceEquals(sender, TransferListModel) || args.PropertyName != nameof(TransferListViewModel.Failure))
            return;
        if (!RecordEditorActive && HomePage.Visibility == Visibility.Visible &&
            TransferListModel?.Failure is (AuthenticationRejectedException or AuthenticationContextChangedException))
        {
            NavigateToLogin("ログインを継続できません。もう一度ログインしてください。");
            return;
        }
        UpdateTransferActions();
    }
    private void UpdateTransferActions()
    {
        if (TransferListModel is { } model)
            model.CanRegister = account is not null && transferOpening is null && TransferMethods is { } methods && TransferEditorRules.CanCreate(methods);
    }

}
