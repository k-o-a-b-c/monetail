namespace Monetail.WinUI;

public sealed partial class MainWindow
{
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
        var ledger = await page.ViewModel!.LoadRecoveryMastersAsync(token);
        if (closed || !ReferenceEquals(TransferPage, page) || page.ViewModel?.AwaitingLogin == true)
            return;
        if (ledger is not null)
            DisplayLedgerHeader(ledger);
        page.RefreshChoices();
        page.RestorePosition(() => !closed && ReferenceEquals(TransferPage, page) && !page.ViewModel!.AwaitingLogin &&
            IsEditorPageVisible && page.ViewModel.Context is { } context && EditorContextMatches(context));
    }
}
