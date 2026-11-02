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
        // 製品起動との接続までは、既存のホーム表示を維持する。
        ShowPage(HomePage);
    }

    /// <summary>APIから受け取った表示用データを反映する。サンプルを通常起動へ埋め込まない。</summary>
    public void DisplayWorkspace(LedgerInfo ledger, PeopleInfo people)
    {
        DisplayLedger(ledger);
        DisplayPeople(people);
    }

    private void DisplayLedger(LedgerInfo ledger)
    {
        if (currentLedger?.LedgerId != ledger.LedgerId)
        {
            PeopleList.ItemsSource = null;
            EmptyPeople.Text = "人物一覧を読み込んでいます。";
            EmptyPeople.Visibility = Visibility.Visible;
        }
        currentLedger = ledger;
        LedgerName.Text = ledger.Name;
        LedgerSelector.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty, ledger.Name);
        Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(LedgerSelector, ledger.Name);
        LedgerContext.Text = ledger.Name;
        LedgerSelector.IsEnabled = account is not null;
        AccountNavigation.IsEnabled = LogoutNavigation.IsEnabled = account is not null;
        ShowPage(HomePage);
    }

    private void DisplayPeople(PeopleInfo people)
    {
        PeopleList.ItemsSource = people.People.OrderByDescending(person => person.PersonId == people.MyPersonId)
            .ThenBy(person => person.DisplayOrder)
            .Select(person => new PersonRow(person, person.PersonId == people.MyPersonId)).ToArray();
        EmptyPeople.Text = "人物は登録されていません。";
        EmptyPeople.Visibility = people.People.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowRecord(object sender, RoutedEventArgs e)
    {
        ShowPage(HomePage);
    }

    private async void ShowManagement(object sender, RoutedEventArgs e)
    {
        ShowPage(ManagementPage);
        PeopleContent.Visibility = Visibility.Visible;
        ConnectionSettingsContent.Visibility = AccountContent.Visibility = Visibility.Collapsed;
        var alreadySelected = ReferenceEquals(ManagementNavigation.SelectedItem, PeopleNavigation);
        ManagementNavigation.SelectedItem = PeopleNavigation;
        if (alreadySelected) await LoadPeopleAsync();
    }
}
