using Monetail.Windows.Client.Transfers;
using Monetail.Windows.Presentation.Transfers;

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
    private async Task TransferSavedAsync(TransferWriteCompletion completion, bool returnToList)
    {
        var request = completion.Request;
        var snapshot = completion.Snapshot;
        var deleted = request.Operation == TransferWriteOperation.Delete;
        if (closed || TransferPage?.ViewModel?.Context is not { } context || !EditorContextMatches(context))
            return;
        // 一覧条件の調整と保存行探索だけをShellで調停する。残高連動は未実装。
        if (!deleted && snapshot is not null && recordSearch is not null && TransferMethods is { } methods)
        {
            recordSearch.ApplySavedTransfer(snapshot, methods);
            recordSearch.ApplySharedText(recordSearch.TransferText);
            RefreshReceiptChoices();
            recordHome?.RefreshSearch();
        }
        ResetTransferEditor();
        if (returnToList)
            NavigateWorkspace(WorkspaceScreen.HomePage);
        if (TransferListModel is { } list && recordSearch is not null)
        {
            var receipt = ReceiptListModel?.StartAsync(receiptSearch!.Query(), lifetime.Token) ?? Task.CompletedTask;
            if (deleted)
                await list.StartAsync(recordSearch.TransferQuery(), lifetime.Token);
            else if (completion.SavedId is Guid id)
                await list.ShowSavedAsync(recordSearch.TransferQuery(), id, lifetime.Token);
            await receipt;
        }
    }
}
