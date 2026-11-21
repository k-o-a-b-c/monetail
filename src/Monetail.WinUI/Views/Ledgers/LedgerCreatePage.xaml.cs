using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Monetail.WinUI.Input;

namespace Monetail.WinUI.Views.Ledgers;

public sealed partial class LedgerCreatePage : Page
{
    public LedgerCreatePage()
    {
        InitializeComponent();
        _ = new FormKeyboard(this, CreateLedgerButton, NotifyCreateAsync, NewLedgerName);
    }

    public event EventHandler<string>? CreateRequested;
    public event RoutedEventHandler? RefreshRequested;
    public event RoutedEventHandler? LogoutRequested;
    public event RoutedEventHandler? CancelRequested;

    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        var emptyMembership = (bool)args.Parameter;
        NoLedgersNotice.Visibility = RefreshLedgersButton.Visibility = CreatePageLogoutButton.Visibility =
            emptyMembership ? Visibility.Visible : Visibility.Collapsed;
        CancelLedgerCreation.Visibility = emptyMembership ? Visibility.Collapsed : Visibility.Visible;
    }

    public void FocusName() => NewLedgerName.Focus(FocusState.Programmatic);

    private Task NotifyCreateAsync()
    {
        CreateRequested?.Invoke(this, NewLedgerName.Text);
        return Task.CompletedTask;
    }

    private void RequestCreate(object sender, RoutedEventArgs args) => _ = NotifyCreateAsync();
    private void RequestRefresh(object sender, RoutedEventArgs args) => RefreshRequested?.Invoke(this, args);
    private void RequestLogout(object sender, RoutedEventArgs args) => LogoutRequested?.Invoke(this, args);
    private void RequestCancel(object sender, RoutedEventArgs args) => CancelRequested?.Invoke(this, args);
}
