using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Monetail.WinUI.Views.Connection;

public sealed partial class FailurePage : Page
{
    public FailurePage() => InitializeComponent();

    public event RoutedEventHandler? RetryRequested;
    public event RoutedEventHandler? ConnectionChangeRequested;
    public event RoutedEventHandler? ExitRequested;

    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        FailureMessage.Text = (string)args.Parameter;
    }

    private void RequestRetry(object sender, RoutedEventArgs args) => RetryRequested?.Invoke(this, args);
    private void RequestConnectionChange(object sender, RoutedEventArgs args) => ConnectionChangeRequested?.Invoke(this, args);
    private void RequestExit(object sender, RoutedEventArgs args) => ExitRequested?.Invoke(this, args);
}
