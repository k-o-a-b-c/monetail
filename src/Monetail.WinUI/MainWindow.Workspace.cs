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
        ReleaseConnectionSettings();
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
                management = new(() => !busy && !ManagementDialogOpen, SelectManagementAsync)
                {
                    LedgerName = currentLedger?.Name ?? "家計簿が開かれていません",
                    HasAccount = account is not null,
                    PeopleEnabled = CanViewPeople,
                    CategoriesEnabled = CanViewCategories,
                    ParticipantsVisible = CanManageParticipants
                };
                WorkspaceHost.Navigate(typeof(ManagementPage), management);
                var page = (ManagementPage)WorkspaceHost.Content;
                page.SetDetails(ManagementContents);
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
            settings.Release();
        if (WorkspaceHost.Content is LedgerChoiceView choices)
            choices.OpenRequested -= OpenSelectedLedger;
        WorkspaceHost.Content = null;
        management = null;
    }

    private void HideWorkspace()
    {
        SuspendRecordReads();
        if (!RecordEditorActive || !EditorAwaitingLogin)
            ReleaseWorkspace();
        WorkspaceHost.Visibility = WorkspaceHeader.Visibility = Visibility.Collapsed;
    }
}
