using Microsoft.UI.Xaml;
using Monetail.Windows.Client.Transport;
using Windows.Graphics;

namespace Monetail.WinUI;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = "Monetail";
        AppWindow.Resize(new SizeInt32(1100, 780));
    }

    /// <summary>APIから受け取った表示用データを反映する。サンプルを通常起動へ埋め込まない。</summary>
    public void DisplayWorkspace(LedgerInfo ledger, PeopleInfo people)
    {
        LedgerName.Text = ledger.Name;
        LedgerSelector.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty, ledger.Name);
        Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(LedgerSelector, ledger.Name);
        LedgerContext.Text = ledger.Name;
        PeopleList.ItemsSource = people.People.OrderByDescending(person => person.PersonId == people.MyPersonId)
            .ThenBy(person => person.DisplayOrder)
            .Select(person => new PersonRow(person, person.PersonId == people.MyPersonId)).ToArray();
        EmptyPeople.Text = "人物は登録されていません。";
        EmptyPeople.Visibility = people.People.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowRecord(object sender, RoutedEventArgs e)
    {
        HomePage.Visibility = Visibility.Visible;
        ManagementPage.Visibility = Visibility.Collapsed;
    }

    private void ShowManagement(object sender, RoutedEventArgs e)
    {
        HomePage.Visibility = Visibility.Collapsed;
        ManagementPage.Visibility = Visibility.Visible;
        ManagementNavigation.SelectedItem = PeopleNavigation;
    }
}
