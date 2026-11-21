using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Navigation;
using Monetail.WinUI.Input;
using Monetail.Windows.Client.Ledgers;

namespace Monetail.WinUI.Views.Ledgers;

public sealed partial class LedgerCreatePage : Page
{
    private readonly FormKeyboard keyboard;

    public LedgerCreatePage()
    {
        InitializeComponent();
        keyboard = new FormKeyboard(this, CreateLedgerButton, NotifyCreateAsync, NewLedgerName);
    }

    public event EventHandler<string>? CreateRequested;
    public event RoutedEventHandler? RefreshRequested;
    public event RoutedEventHandler? LogoutRequested;
    public event RoutedEventHandler? CancelRequested;

    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        var emptyMembership = (bool)args.Parameter;
        PageTitle.Text = emptyMembership ? "家計簿がありません" : "家計簿の作成";
        foreach (var element in new FrameworkElement[] { NoLedgersNotice, CreateSectionTitle, InvitationDivider,
            InvitationSection, RefreshLedgersButton, LogoutDivider, CreatePageLogoutButton })
            element.Visibility = emptyMembership ? Visibility.Visible : Visibility.Collapsed;
        CancelLedgerCreation.Visibility = emptyMembership ? Visibility.Collapsed : Visibility.Visible;
    }

    public void ShowNameError(string message)
    {
        LedgerNameError.Text = message;
        LedgerNameError.Visibility = Visibility.Visible;
        AutomationProperties.SetHelpText(NewLedgerName, message);
        NewLedgerName.Focus(FocusState.Programmatic);
    }

    private void NameChanged(object sender, TextChangedEventArgs args)
    {
        if (LedgerNameError is null || LedgerNameError.Visibility != Visibility.Visible)
            return;
        try
        {
            LedgerCreationDraft.ValidateName(NewLedgerName.Text);
            LedgerNameError.Text = "";
            LedgerNameError.Visibility = Visibility.Collapsed;
            AutomationProperties.SetHelpText(NewLedgerName, "");
        }
        catch (FormatException)
        {
            // 検証の正本はClient。同じ不正入力なら既存エラーを維持する。
        }
    }

    public void ShowEmptyMembership() => MembershipResult.Visibility = Visibility.Visible;

    private Task NotifyCreateAsync()
    {
        CreateRequested?.Invoke(this, NewLedgerName.Text);
        return Task.CompletedTask;
    }

    private void RequestCreate(object sender, RoutedEventArgs args) => _ = NotifyCreateAsync();
    private void RequestRefresh(object sender, RoutedEventArgs args)
    {
        MembershipResult.Visibility = Visibility.Collapsed;
        RefreshRequested?.Invoke(this, args);
    }
    private void RequestLogout(object sender, RoutedEventArgs args) => LogoutRequested?.Invoke(this, args);
    private void RequestCancel(object sender, RoutedEventArgs args) => CancelRequested?.Invoke(this, args);
}
