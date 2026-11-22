using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Monetail.Windows.Presentation.Connection;
using Monetail.WinUI.Views.Connection;

namespace Monetail.WinUI.Views.Management;

public sealed partial class ConnectionSettingsPage : Page
{
    public ConnectionSettingsPage() => InitializeComponent();
    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        ConnectionSettingsContent.Content = new ConnectionSettingsView((ConnectionSettingsViewModel)args.Parameter);
    }
    public void Release()
    {
        (ConnectionSettingsContent.Content as ConnectionSettingsView)?.ViewModel.Dispose();
        ConnectionSettingsContent.Content = null;
    }
}
