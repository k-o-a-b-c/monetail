using Monetail.Windows.Presentation.Receipts;

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
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
        var ledger = await page.ViewModel!.LoadRecoveryMastersAsync(token);
        if (closed || !ReferenceEquals(ReceiptPage, page) || page.ViewModel?.AwaitingLogin == true)
            return;
        if (ledger is not null)
            DisplayLedgerHeader(ledger);
        page.RefreshMasters(true);
        page.RefreshRecovery();
        page.RestorePosition(() => !closed && ReferenceEquals(ReceiptPage, page) && !page.ViewModel!.AwaitingLogin &&
            IsEditorPageVisible && page.ViewModel.Context is { } context && EditorContextMatches(context));
    }
}
