using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Monetail.Windows.Presentation.Workspace;
public enum ManagementSection
{
    People,
    Categories,
    Payments,
    Participants,
    Connection,
    Account,
    Logout
}

/// <summary>管理メニューの選択・表示可否を所有する。取得/遷移の承認はShellへ通知する。</summary>
public sealed partial class ManagementViewModel(Func<bool> canNavigate, Func<ManagementSection, Task> select) : ObservableObject
{
    private ManagementSection selectedSection;
    public ManagementSection SelectedSection { get => selectedSection; set => SetProperty(ref selectedSection, value); }

    private string ledgerName = "家計簿が開かれていません";
    public string LedgerName { get => ledgerName; set => SetProperty(ref ledgerName, value); }

    private bool hasAccount, peopleEnabled, participantsVisible, categoriesEnabled;
    public bool HasAccount { get => hasAccount; set => SetProperty(ref hasAccount, value); }
    public bool PeopleEnabled { get => peopleEnabled; set => SetProperty(ref peopleEnabled, value); }
    public bool ParticipantsVisible { get => participantsVisible; set => SetProperty(ref participantsVisible, value); }
    public bool CategoriesEnabled { get => categoriesEnabled; set => SetProperty(ref categoriesEnabled, value); }

    private bool CanSelect(ManagementSection section) => canNavigate();
    [RelayCommand(CanExecute = nameof(CanSelect))]
    private Task SelectAsync(ManagementSection section) => canNavigate() ? select(section) : Task.CompletedTask;
}
