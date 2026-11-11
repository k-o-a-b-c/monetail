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
        InitializeReceipts();
        InitializeReceiptEditor();
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
        ResetReceipts();
        ResetCategories();
        ResetPayments();
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
        if (currentLedger is { } ledger) connection.Masters.UpdatePeople(ledger.LedgerId, people);
        peopleInfo = people;
        peoplePermissions = new(people.CurrentRole, people.MyPersonId);
        missingPeople.Clear();
        RenderPeopleRows();
        UpdatePeopleActions();
    }

    private void RenderPeopleRows()
    {
        if (peopleInfo is null) return;
        var selectedId = (PeopleList.SelectedItem as PersonRow)?.Person.PersonId;
        var today = DateOnly.FromDateTime(DateTime.Now);
        var rows = peopleInfo.People.Where(person => IncludeInactivePeople.IsChecked == true ||
                (person.ValidFrom is null || person.ValidFrom <= today) && (person.ValidTo is null || person.ValidTo >= today))
            .OrderByDescending(person => person.PersonId == peoplePermissions?.MyPersonId)
            .ThenBy(person => person.DisplayOrder)
            .Select(person => new PersonRow(person, person.PersonId == peoplePermissions?.MyPersonId)).ToArray();
        PeopleList.ItemsSource = rows;
        PeopleList.SelectedItem = rows.FirstOrDefault(row => row.Person.PersonId == selectedId);
        EmptyPeople.Text = "表示対象の人物はありません。";
        EmptyPeople.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdatePeopleActions();
    }

    private void FilterPeople(object sender, RoutedEventArgs args) => RenderPeopleRows();

    private async void ShowRecord(object sender, RoutedEventArgs e)
    {
        if (busy || ManagementDialogOpen || !await LeaveReceiptEditorAsync()) return;
        DropPendingCategoryEdit(); DiscardCategoryEditor(); DiscardCategoryOrder();
        DropPendingPaymentEdit(); DiscardPaymentEditor(); DiscardPaymentOrder();
        DropPendingPersonEdit();
        DiscardPersonEditor();
        DiscardPersonOrder();
        ShowPage(HomePage);
    }

    private async void ShowManagement(object sender, RoutedEventArgs e)
    {
        if (busy || ManagementDialogOpen || !await LeaveReceiptEditorAsync()) return;
        DiscardCategoryOrder(); CategoriesContent.Visibility = Visibility.Collapsed;
        DiscardPaymentOrder(); PaymentsContent.Visibility = Visibility.Collapsed;
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
