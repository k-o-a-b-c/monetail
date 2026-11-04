using Microsoft.UI.Xaml;
using Monetail.Windows.Client.Transport;
using Windows.Graphics;

namespace Monetail.WinUI;

public sealed partial class MainWindow : Window
{
    public MainWindow()
        : this(ClientConnection.CreateDefault()) { }

    internal MainWindow(ClientConnection connection)
    {
        this.connection = connection;
        InitializeComponent();
        Title = "Monetail";
        AppWindow.Resize(new SizeInt32(1100, 780));
        Closed += CloseConnection;
        InitializeInput();
        ShowPage(StartupPage);
    }

    /// <summary>APIから受け取った表示用データを反映する。サンプルを通常起動へ埋め込まない。</summary>
    public void DisplayWorkspace(LedgerInfo ledger, PeopleInfo people)
    {
        DisplayLedger(ledger);
        DisplayPeople(people);
    }

    private void DisplayLedger(LedgerInfo ledger)
    {
        ResetPeople();
        ResetParticipants();
        currentLedger = ledger;
        LedgerName.Text = ledger.Name;
        LedgerSelector.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty, ledger.Name);
        Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(LedgerSelector, ledger.Name);
        LedgerContext.Text = ledger.Name;
        LedgerSelector.IsEnabled = account is not null;
        AccountNavigation.IsEnabled = LogoutNavigation.IsEnabled = account is not null;
        UpdatePeopleActions();
        ShowPage(HomePage);
    }

    private void DisplayPeople(PeopleInfo people)
    {
        peopleInfo = people;
        peoplePermissions = new(people.CurrentRole, people.MyPersonId);
        missingPeople.Clear();
        RenderPeopleRows();
        EmptyPeople.Text = "人物は登録されていません。";
        EmptyPeople.Visibility = people.People.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdatePeopleActions();
    }

    private void RenderPeopleRows()
    {
        if (peopleInfo is null) return;
        PeopleList.ItemsSource = peopleInfo.People.OrderByDescending(person => person.PersonId == peoplePermissions?.MyPersonId)
            .ThenBy(person => person.DisplayOrder)
            .Select(person => new PersonRow(person, person.PersonId == peoplePermissions?.MyPersonId)).ToArray();
    }

    private void ShowRecord(object sender, RoutedEventArgs e)
    {
        if (busy || ManagementDialogOpen) return;
        DropPendingPersonEdit();
        DiscardPersonEditor();
        DiscardPersonOrder();
        ShowPage(HomePage);
    }

    private async void ShowManagement(object sender, RoutedEventArgs e)
    {
        if (busy || ManagementDialogOpen) return;
        DiscardPersonOrder();
        ParticipantsContent.Visibility = InvitationsContent.Visibility = Visibility.Collapsed;
        ShowPage(ManagementPage);
        if (!CanViewPeople)
        {
            ManagementNavigation.SelectedItem = AccountNavigation;
            AccountName.Text = account?.DisplayName ?? "";
            AccountServer.Text = connection.Endpoint?.Value ?? "";
            AccountContent.Visibility = Visibility.Visible;
            PeopleContent.Visibility = ConnectionSettingsContent.Visibility = Visibility.Collapsed;
            return;
        }
        PeopleContent.Visibility = Visibility.Visible;
        ConnectionSettingsContent.Visibility = AccountContent.Visibility = Visibility.Collapsed;
        var alreadySelected = ReferenceEquals(ManagementNavigation.SelectedItem, PeopleNavigation);
        ManagementNavigation.SelectedItem = PeopleNavigation;
        if (alreadySelected) await LoadPeopleAsync();
    }
}
