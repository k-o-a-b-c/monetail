using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Monetail.WinUI.Views.Authentication;

public sealed partial class LoginPage : Page
{
    public LoginPage() => InitializeComponent();

    public event RoutedEventHandler? LoginRequested;
    public event RoutedEventHandler? ConnectionChangeRequested;
    public event RoutedEventHandler? BrowserOpenRequested;
    public event RoutedEventHandler? CancelRequested;

    public void SetAuthenticationState(bool waiting, bool canInteract)
    {
        LoginContent.Visibility = waiting ? Visibility.Collapsed : Visibility.Visible;
        WaitingContent.Visibility = waiting ? Visibility.Visible : Visibility.Collapsed;
        LoginButton.IsEnabled = canInteract;
        LoginChangeConnection.IsEnabled = canInteract;
        ReopenBrowserButton.IsEnabled = canInteract;
        CancelLoginButton.IsEnabled = canInteract;
    }

    private void RequestLogin(object sender, RoutedEventArgs args) => LoginRequested?.Invoke(this, args);
    private void RequestConnectionChange(object sender, RoutedEventArgs args) => ConnectionChangeRequested?.Invoke(this, args);
    private void RequestBrowserOpen(object sender, RoutedEventArgs args) => BrowserOpenRequested?.Invoke(this, args);
    private void RequestCancel(object sender, RoutedEventArgs args) => CancelRequested?.Invoke(this, args);
}
