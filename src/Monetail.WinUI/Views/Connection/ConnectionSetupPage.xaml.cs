using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Monetail.Windows.Presentation.Connection;

namespace Monetail.WinUI.Views.Connection;

public sealed partial class ConnectionSetupPage : Page
{
    public ConnectionSetupPage() => InitializeComponent();
    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        SettingsHost.Content = new ConnectionSettingsView((ConnectionSettingsViewModel)args.Parameter);
    }
    public void Release()
    {
        (SettingsHost.Content as ConnectionSettingsView)?.ViewModel.Dispose();
        SettingsHost.Content = null;
    }
    protected override void OnNavigatedFrom(NavigationEventArgs args)
    {
        Release();
        base.OnNavigatedFrom(args);
    }
}
