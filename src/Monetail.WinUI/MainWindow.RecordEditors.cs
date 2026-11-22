using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using Monetail.Windows.Presentation.Records;
using Monetail.Windows.Presentation.Receipts;

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
    private bool IsEditorLeaveTarget(DependencyObject? element)
    {
        for (; element is not null; element = VisualTreeHelper.GetParent(element))
            if (ReferenceEquals(element, SettingsButton) || ReferenceEquals(element, RecordButton) || ReferenceEquals(element, LedgerSelector))
                return true;
        return false;
    }
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
    private void ShowRetainedEditor()
    {
        ReleaseAuthentication();
        ReleaseLedgerPage();
        WorkspaceHost.Visibility = WorkspaceHeader.Visibility = Visibility.Visible;
    }
}
