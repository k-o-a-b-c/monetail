using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Monetail.Windows.Presentation.Workspace;
using Monetail.WinUI.Views.Management;

namespace Monetail.WinUI.Views.Workspace;
public sealed partial class ManagementPage : Page
{
    public ManagementViewModel ViewModel { get; private set; } = null!;
    public bool HasDialog => Feature?.HasDialog == true;
    public bool CanRestoreAuthentication => Feature?.CanRestoreAuthentication == true;

    public event EventHandler? ReturnRequested;
    private IManagementFeature? Feature => DetailsHost.Content as IManagementFeature;

    public ManagementPage() => InitializeComponent();
    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        ViewModel = (ManagementViewModel)args.Parameter;
        ViewModel.PropertyChanged += ModelChanged;
        ViewModel.NavigationRequested += OpenSectionAsync;
        Bindings.Update();
        RenderSelection();
    }

    public async Task OpenSectionAsync(ManagementSection section)
    {
        if (HasDialog || ViewModel.Context.IsBusy)
            return;
        if (section == ManagementSection.Logout)
        {
            await OpenSectionAsync(ManagementSection.Account);
            await ViewModel.LogoutAsync();
            return;
        }

        ReleaseDetails();
        ViewModel.SelectedSection = section;
        RenderSelection();
        switch (section)
        {
            case ManagementSection.People:
                DetailsHost.Navigate(typeof(PeoplePage), ViewModel.People);
                break;
            case ManagementSection.Categories:
                DetailsHost.Navigate(typeof(CategoriesPage), ViewModel.Categories);
                break;
            case ManagementSection.Payments:
                DetailsHost.Navigate(typeof(PaymentMethodsPage), ViewModel.Payments);
                break;
            case ManagementSection.Participants:
                if (!ViewModel.ParticipantsVisible)
                    return;
                DetailsHost.Navigate(typeof(ParticipantsPage), ViewModel.Participants);
                break;
            case ManagementSection.Connection:
                DetailsHost.Navigate(typeof(ConnectionSettingsPage), ViewModel.CreateConnectionSettings());
                break;
            case ManagementSection.Account:
                DetailsHost.Navigate(typeof(AccountPage), ViewModel);
                break;
        }

        if (Feature is { } feature)
        {
            feature.ReturnRequested += ReturnFromFeature;
            await feature.LoadAsync();
        }
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ManagementViewModel.SelectedSection))
            RenderSelection();
    }

    private void RenderSelection() => ManagementNavigation.SelectedItem = ManagementNavigation.MenuItems.OfType<NavigationViewItem>().Single(item => Equals(item.Tag, ViewModel.SelectedSection.ToString()));
    private async void SelectManagement(object sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (ViewModel is null || args.SelectedItem is not NavigationViewItem { Tag: string value } || !Enum.TryParse<ManagementSection>(value, out var section) || section == ViewModel.SelectedSection)
            return;
        if (!ViewModel.SelectCommand.CanExecute(section))
        {
            RenderSelection();
            return;
        }

        await ViewModel.SelectCommand.ExecuteAsync(section);
    }

    public bool SuspendForAuthentication()
    {
        if (ViewModel.Context.IsSuspended)
            return true;
        if (!CanRestoreAuthentication)
            return false;
        ViewModel.Context.IsSuspended = true;
        Feature?.SuspendForAuthentication();
        return true;
    }

    public async Task ResumeAsync(string? denied, CancellationToken token)
    {
        if (Feature is { } feature)
            await feature.ResumeAsync(denied, token);
        ViewModel.Context.IsSuspended = false;
    }

    private void ReturnFromFeature(object? sender, EventArgs args) => ReturnRequested?.Invoke(this, EventArgs.Empty);

    private void ReleaseDetails()
    {
        if (Feature is { } feature)
        {
            feature.ReturnRequested -= ReturnFromFeature;
            feature.Release();
        }
        (DetailsHost.Content as ConnectionSettingsPage)?.Release();
        DetailsHost.Content = null;
    }

    private Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public void Release()
    {
        if (ViewModel is not null)
        {
            ViewModel.PropertyChanged -= ModelChanged;
            ViewModel.NavigationRequested -= OpenSectionAsync;
        }

        ReleaseDetails();
        ReturnRequested = null;
        Bindings.StopTracking();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs args)
    {
        Release();
        base.OnNavigatedFrom(args);
    }
}
