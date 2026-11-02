using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Client;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
    private LedgerInfo? currentLedger;
    private IReadOnlyList<LedgerInfo> ledgerChoices = [];
    private LedgerCreationDraft? creationDraft;
    private MenuFlyout? ledgerMenu;

    private async Task OpenDestinationAsync(CancellationToken token)
    {
        var destination = await connection.Ledgers.StartAsync(token);
        ledgerChoices = destination.Choices;
        if (destination.Opened is not null) DisplayLedger(destination.Opened);
        else if (ledgerChoices.Count == 0) ShowCreation();
        else
        {
            LedgerChoices.ItemsSource = ledgerChoices;
            OpenLedgerButton.IsEnabled = false;
            ShowPage(LedgerChoicePage);
        }
    }

    private void ClearLedger()
    {
        currentLedger = null;
        ledgerChoices = [];
        creationDraft = null;
        LedgerChoices.ItemsSource = PeopleList.ItemsSource = null;
        ledgerMenu = null;
        LedgerSelector.IsEnabled = false;
        LedgerName.Text = "家計簿を選択";
        LedgerContext.Text = "家計簿が開かれていません";
    }

    private void ShowCreation()
    {
        creationDraft = null;
        NewLedgerName.Text = "";
        NoLedgersNotice.Visibility = currentLedger is null && ledgerChoices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CancelLedgerCreation.Visibility = currentLedger is not null || ledgerChoices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowPage(LedgerCreatePage);
    }

    private async void CreateLedger(object sender, RoutedEventArgs args)
    {
        try
        {
            var name = NewLedgerName.Text.Trim();
            if (creationDraft?.Name != name) creationDraft = new LedgerCreationDraft(name);
        }
        catch (FormatException error) { ShowError(error.Message); NewLedgerName.Focus(FocusState.Programmatic); return; }
        var draft = creationDraft;
        await RunAsync(async () =>
        {
            var opened = await connection.Ledgers.CreateAsync(draft!, lifetime.Token);
            creationDraft = null;
            DisplayLedger(opened);
        }, () => Task.CompletedTask);
    }

    private void CancelCreation(object sender, RoutedEventArgs args)
    {
        creationDraft = null;
        ShowPage(currentLedger is null ? LedgerChoicePage : HomePage);
    }

    private void ChooseLedger(object sender, SelectionChangedEventArgs args) =>
        OpenLedgerButton.IsEnabled = LedgerChoices.SelectedItem is LedgerInfo;

    private async void OpenSelectedLedger(object sender, RoutedEventArgs args)
    {
        if (LedgerChoices.SelectedItem is LedgerInfo selected) await OpenLedgerAsync(selected.LedgerId);
    }

    private Task OpenLedgerAsync(Guid ledgerId) => RunAsync(async () =>
    {
        var opened = await connection.Ledgers.OpenAsync(ledgerId, lifetime.Token);
        PeopleList.ItemsSource = null;
        EmptyPeople.Text = "人物一覧を読み込んでいます。";
        EmptyPeople.Visibility = Visibility.Visible;
        DisplayLedger(opened);
    }, () => OpenLedgerAsync(ledgerId));

    private async void ShowLedgerMenu(object sender, RoutedEventArgs args)
    {
        ledgerMenu = null;
        await RunAsync(async () =>
        {
            var choices = await connection.Ledgers.ListAsync(lifetime.Token);
            var menu = new MenuFlyout();
            foreach (var ledger in choices)
            {
                var item = new MenuFlyoutItem { Text = ledger.Name };
                item.Click += async (_, _) => await OpenLedgerAsync(ledger.LedgerId);
                menu.Items.Add(item);
            }
            menu.Items.Add(new MenuFlyoutSeparator());
            var create = new MenuFlyoutItem { Text = "＋追加" };
            create.Click += (_, _) => ShowCreation();
            menu.Items.Add(create);
            ledgerMenu = menu;
        }, () => Task.CompletedTask);
        if (!closed && HomePage.Visibility == Visibility.Visible) ledgerMenu?.ShowAt(LedgerSelector);
    }

    private Task LoadPeopleAsync()
    {
        if (account is null || currentLedger is null) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            var workspace = new PeopleWorkspace(connection.Api, connection.Session, currentLedger.LedgerId);
            DisplayPeople(await workspace.ListAsync(lifetime.Token));
        }, LoadPeopleAsync);
    }

    private async void SelectManagement(object sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item || busy) return;
        PeopleContent.Visibility = ConnectionSettingsContent.Visibility = AccountContent.Visibility = Visibility.Collapsed;
        if (ReferenceEquals(item, PeopleNavigation))
        {
            PeopleContent.Visibility = Visibility.Visible;
            await LoadPeopleAsync();
        }
        else if (ReferenceEquals(item, ConnectionNavigation))
        {
            SettingsServerUrl.Text = connection.Endpoint?.Value ?? "";
            ConnectionSettingsContent.Visibility = Visibility.Visible;
        }
        else if (ReferenceEquals(item, AccountNavigation))
        {
            AccountName.Text = account?.DisplayName ?? "";
            AccountServer.Text = connection.Endpoint?.Value ?? "";
            AccountContent.Visibility = Visibility.Visible;
        }
        else if (ReferenceEquals(item, LogoutNavigation))
        {
            // 失敗時も本人と開いている家計簿を保持する。
            ManagementNavigation.SelectedItem = AccountNavigation;
            AccountContent.Visibility = Visibility.Visible;
            AccountName.Text = account?.DisplayName ?? "";
            AccountServer.Text = connection.Endpoint?.Value ?? "";
            await LogoutAsync();
        }
    }

    private async void ConnectSettings(object sender, RoutedEventArgs args)
    {
        ApiEndpoint candidate;
        try { candidate = ApiEndpoint.Parse(SettingsServerUrl.Text); }
        catch (FormatException error) { ShowError(error.Message); SettingsServerUrl.Focus(FocusState.Programmatic); return; }
        await ConnectAsync(candidate);
    }
}
