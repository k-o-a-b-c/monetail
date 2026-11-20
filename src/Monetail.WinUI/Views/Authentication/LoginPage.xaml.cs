using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Monetail.WinUI.Views.Authentication;

public sealed partial class LoginPage : Page
{
    public LoginPage() => InitializeComponent();

    public event RoutedEventHandler? LoginRequested;
    public event RoutedEventHandler? ConnectionChangeRequested;

    private void RequestLogin(object sender, RoutedEventArgs args) => LoginRequested?.Invoke(this, args);
    private void RequestConnectionChange(object sender, RoutedEventArgs args) => ConnectionChangeRequested?.Invoke(this, args);
}
