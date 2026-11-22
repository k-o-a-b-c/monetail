using Microsoft.UI.Xaml;
using Monetail.Windows.Presentation.Workspace;
using Monetail.WinUI.Views;
using Monetail.WinUI.Views.Workspace;

namespace Monetail.WinUI;
public sealed partial class MainWindow
{
    private enum WorkspaceScreen
    {
        HomePage,
        ManagementPage,
        LedgerChoicePage
    }

    private RecordHomePage? RecordPage => WorkspaceHost.Content as RecordHomePage;

    private ManagementViewModel? management;
    private bool IsRecordPageVisible => WorkspaceHost.Visibility == Visibility.Visible && RecordPage is not null;

    private void NavigateWorkspace(WorkspaceScreen screen)
    {
        if (closed || RecordEditorActive && screen != WorkspaceScreen.HomePage || EditorAwaitingLogin && screen == WorkspaceScreen.HomePage)
            return;
        ReleaseAuthentication();
        ReleaseLedgerPage();
        WorkspaceHost.Visibility = Visibility.Visible;
        WorkspaceHeader.Visibility = screen == WorkspaceScreen.LedgerChoicePage || currentLedger is null ? Visibility.Collapsed : Visibility.Visible;
        RecordSelectionIndicator.Visibility = screen == WorkspaceScreen.HomePage ? Visibility.Visible : Visibility.Collapsed;
        RecordButton.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.ItemStatusProperty,
            screen == WorkspaceScreen.HomePage ? "選択中" : "");
        SettingsButton.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.ItemStatusProperty,
            screen == WorkspaceScreen.ManagementPage ? "選択中" : "");
        ErrorNotice.IsOpen = false;
        ErrorNotice.Visibility = Visibility.Collapsed;
        if (screen == WorkspaceScreen.HomePage && RecordPage is { } current)
        {
            if (!RecordEditorActive)
                EnsureRecordHome();
            current.SetViewModel(recordHome);
            current.Resume();
            return;
        }

        if (screen == WorkspaceScreen.ManagementPage && ManagementPage is not null)
            return;

        SuspendRecordReads();
        ReleaseWorkspace();
        WorkspaceHost.Visibility = Visibility.Visible;
        switch (screen)
        {
            case WorkspaceScreen.HomePage:
                EnsureRecordHome();
                WorkspaceHost.Navigate(typeof(RecordHomePage), recordHome);
                ((RecordHomePage)WorkspaceHost.Content).Failed += WorkspaceFailed;
                break;
            case WorkspaceScreen.ManagementPage:
                EnsureManagement();
                WorkspaceHost.Navigate(typeof(ManagementPage), management);
                ((ManagementPage)WorkspaceHost.Content).ReturnRequested += ReturnFromManagement;
                break;
            case WorkspaceScreen.LedgerChoicePage:
                WorkspaceHost.Navigate(typeof(LedgerChoiceView));
                var choices = (LedgerChoiceView)WorkspaceHost.Content;
                choices.Ledgers = ledgerChoices;
                choices.OpenRequested += OpenSelectedLedger;
                break;
        }
    }

    private void WorkspaceFailed(object? sender, string message)
    {
        if (!closed && ReferenceEquals(sender, RecordPage) && IsRecordPageVisible)
            ShowError(message);
    }

    private void ReleaseWorkspace()
    {
        if (WorkspaceHost.Content is RecordHomePage records)
            records.Release();
        if (WorkspaceHost.Content is ManagementPage settings)
        {
            settings.ReturnRequested -= ReturnFromManagement;
            settings.Release();
        }
        if (WorkspaceHost.Content is LedgerChoiceView choices)
            choices.OpenRequested -= OpenSelectedLedger;
        WorkspaceHost.Content = null;
    }

    private void HideWorkspace()
    {
        SuspendRecordReads();
        if ((!RecordEditorActive || !EditorAwaitingLogin) && management?.Context.IsSuspended != true)
            ReleaseWorkspace();
        WorkspaceHost.Visibility = WorkspaceHeader.Visibility = Visibility.Collapsed;
    }
}
