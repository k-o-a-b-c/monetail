using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monetail.Windows.Client;
using Monetail.Windows.Presentation.Connection;
using Monetail.Windows.Presentation.Management;

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

/// <summary>家計簿内の管理状態を所有する。Pageやダイアログの参照は持たない。</summary>
public sealed partial class ManagementViewModel : ObservableObject, IDisposable
{
    private readonly Func<bool> canNavigate;
    private readonly Func<ApiEndpoint, CancellationToken, Task> connect;
    private readonly Func<Task> logout;
    private PeopleManagementViewModel? people;
    private CategoriesManagementViewModel? categories;
    private PaymentMethodsManagementViewModel? payments;
    private ParticipantsManagementViewModel? participants;
    public ManagementContext Context { get; }
    public string AccountName { get; }
    public string LedgerName => Context.Ledger.Name;
    public bool HasAccount => !Context.IsSuspended;
    public bool PeopleEnabled => people?.CanView ?? Context.Permissions.CurrentRole is not null;
    public bool CategoriesEnabled => categories?.CanView ?? Context.Permissions.CurrentRole is not null;
    public bool ParticipantsVisible => participants?.CanManage ?? Context.Permissions.CurrentRole == "Admin";
    public PeopleManagementViewModel People => people ??= Observe(new PeopleManagementViewModel(Context));
    public CategoriesManagementViewModel Categories => categories ??= Observe(new CategoriesManagementViewModel(Context));
    public PaymentMethodsManagementViewModel Payments => payments ??= Observe(new PaymentMethodsManagementViewModel(Context));
    public ParticipantsManagementViewModel Participants => participants ??= Observe(new ParticipantsManagementViewModel(Context));

    [ObservableProperty]
    public partial ManagementSection SelectedSection { get; set; }

    public event Func<ManagementSection, Task>? NavigationRequested;
    public ManagementViewModel(ManagementContext context, Func<bool> canNavigate, Func<ApiEndpoint, CancellationToken, Task> connect, Func<Task> logout, string accountName)
    {
        Context = context;
        this.canNavigate = canNavigate;
        this.connect = connect;
        this.logout = logout;
        AccountName = accountName;
        Context.PropertyChanged += StateChanged;
    }

    private T Observe<T>(T model)
        where T : ObservableObject
    {
        model.PropertyChanged += StateChanged;
        return model;
    }

    private void StateChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender != Context && args.PropertyName is not ("CanView" or "CanManage"))
            return;
        OnPropertyChanged(nameof(LedgerName));
        OnPropertyChanged(nameof(HasAccount));
        OnPropertyChanged(nameof(PeopleEnabled));
        OnPropertyChanged(nameof(CategoriesEnabled));
        OnPropertyChanged(nameof(ParticipantsVisible));
        SelectCommand.NotifyCanExecuteChanged();
    }

    private bool CanSelect(ManagementSection section) => canNavigate();
    [RelayCommand(CanExecute = nameof(CanSelect))]
    private Task SelectAsync(ManagementSection section) => canNavigate() ? NavigationRequested?.Invoke(section) ?? Task.CompletedTask : Task.CompletedTask;
    public ConnectionSettingsViewModel CreateConnectionSettings() => new(ApiEndpoint.Parse(Context.Origin), connect);
    public Task LogoutAsync() => logout();
    public void Dispose()
    {
        Context.PropertyChanged -= StateChanged;
        if (people is not null)
            people.PropertyChanged -= StateChanged;
        if (payments is not null)
            payments.PropertyChanged -= StateChanged;
        if (categories is not null)
            categories.PropertyChanged -= StateChanged;
        if (participants is not null)
            participants.PropertyChanged -= StateChanged;
        people?.Dispose();
        categories?.Dispose();
        payments?.Dispose();
        participants?.Dispose();
        Context.Dispose();
        NavigationRequested = null;
    }
}
