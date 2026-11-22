using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Monetail.Windows.Presentation.Workspace;

namespace Monetail.WinUI.Views.Management;
public sealed partial class AccountPage : Page
{
    public ManagementViewModel ViewModel { get; private set; } = null!;

    public AccountPage() => InitializeComponent();
    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        ViewModel = (ManagementViewModel)args.Parameter;
        Bindings.Update();
    }
}
