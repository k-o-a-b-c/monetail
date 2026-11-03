using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
    private List<PersonInfo>? orderedPeople;
    private Guid personOrderRevision;
    private bool orderDenied;
    private bool IsOrderingPeople => orderedPeople is not null;
    private sealed record PersonOrderDraft(Guid Revision, PersonInfo[] People);

    private void RestorePersonOrder(PersonOrderDraft draft, PeopleInfo current, string? failure)
    {
        // 表示名は取得済みの最新情報を使い、未保存のID順と元の全体版は変えない。
        var latest = current.People.ToDictionary(person => person.PersonId);
        BeginPersonOrder(draft.People.Select(person => latest.GetValueOrDefault(person.PersonId) ?? person), draft.Revision);
        orderDenied = failure is not null || !IsPeopleAdmin;
        PersonOrderRestoredNotice.Visibility = Visibility.Visible;
        if (orderDenied) ShowPersonOrderNotice(failure ?? "現在の権限では並び順を保存できません。未保存の並び順は残しています。");
    }

    private void OpenPersonOrder(object sender, RoutedEventArgs args)
    {
        if (busy || personDialogOpen || !IsPeopleAdmin || peopleInfo is null) return;
        BeginPersonOrder(peopleInfo.People.OrderBy(person => person.DisplayOrder), peopleInfo.OrderRevision);
    }

    private void BeginPersonOrder(IEnumerable<PersonInfo> people, Guid revision)
    {
        orderedPeople = people.ToList();
        personOrderRevision = revision;
        orderDenied = false;
        PersonOrderNotice.IsOpen = false;
        PersonOrderNotice.Visibility = PersonOrderRestoredNotice.Visibility = Visibility.Collapsed;
        PeopleContent.Visibility = ConnectionSettingsContent.Visibility = AccountContent.Visibility = Visibility.Collapsed;
        PersonOrderContent.Visibility = Visibility.Visible;
        RenderPersonOrder(-1);
    }

    private void RenderPersonOrder(int selectedIndex)
    {
        PersonOrderList.ItemsSource = orderedPeople?.Select(person => new PersonRow(person, person.PersonId == peoplePermissions?.MyPersonId)).ToArray();
        PersonOrderList.SelectedIndex = selectedIndex;
        if (PersonOrderList.SelectedItem is { } selected) PersonOrderList.ScrollIntoView(selected);
        EmptyPersonOrder.Visibility = orderedPeople is { Count: 0 } ? Visibility.Visible : Visibility.Collapsed;
        UpdatePersonOrderActions();
    }

    private void UpdatePersonOrderActions()
    {
        var allowed = !busy && IsOrderingPeople && !orderDenied && account is not null && IsPeopleAdmin;
        var index = PersonOrderList.SelectedIndex;
        SavePersonOrderButton.IsEnabled = allowed;
        MovePersonUpButton.IsEnabled = allowed && index > 0;
        MovePersonDownButton.IsEnabled = allowed && index >= 0 && index < orderedPeople!.Count - 1;
    }

    private void SelectPersonOrder(object sender, SelectionChangedEventArgs args) => UpdatePersonOrderActions();
    private void MovePersonUp(object sender, RoutedEventArgs args) => MovePerson(-1);
    private void MovePersonDown(object sender, RoutedEventArgs args) => MovePerson(1);

    private void MovePerson(int direction)
    {
        if (busy || orderDenied || !IsPeopleAdmin || orderedPeople is null) return;
        var from = PersonOrderList.SelectedIndex;
        var to = from + direction;
        if (from < 0 || to < 0 || to >= orderedPeople.Count) return;
        (orderedPeople[from], orderedPeople[to]) = (orderedPeople[to], orderedPeople[from]);
        RenderPersonOrder(to);
    }

    private async void SavePersonOrder(object sender, RoutedEventArgs args) => await SavePersonOrderAsync();

    private async Task SavePersonOrderAsync()
    {
        if (busy || !SavePersonOrderButton.IsEnabled || orderedPeople is null) return;
        var ids = orderedPeople.Select(person => person.PersonId).ToArray();
        var revision = personOrderRevision;
        PersonOrderNotice.IsOpen = false;
        PersonOrderNotice.Visibility = Visibility.Collapsed;
        await RunAsync(async () =>
        {
            try
            {
                var workspace = new PeopleWorkspace(connection.Api, connection.Session, currentLedger!.LedgerId);
                DisplayPeople(await workspace.SaveOrderAsync(revision, ids, lifetime.Token));
                DiscardPersonOrder();
                PeopleContent.Visibility = Visibility.Visible;
            }
            catch (PeopleRequestException error)
            {
                if (error.Code == "revision_conflict" && error.LatestPeople is { } latest)
                {
                    // 共通順の競合だけは確認済みの契約に従い、その場で最新一覧へ置き換える。
                    DisplayPeople(latest);
                    BeginPersonOrder(latest.People.OrderBy(person => person.DisplayOrder), latest.OrderRevision);
                    ShowPersonOrderNotice("ほかの操作で変更されています。最新の並び順を読み込みます。", InfoBarSeverity.Informational);
                }
                else
                {
                    ApplyPeopleFailureContext(error);
                    if (error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound) orderDenied = true;
                    ShowPersonOrderNotice(error.StatusCode switch
                    {
                        HttpStatusCode.Forbidden => "並び順を変更する権限がありません。変更内容は保存されていません。",
                        HttpStatusCode.NotFound => "この家計簿を操作できません。変更内容は保存されていません。",
                        HttpStatusCode.TooManyRequests => "操作回数の上限に達しました。しばらく待って再試行してください。",
                        _ => "並び順を保存できませんでした。内容を確認してください。"
                    });
                    UpdatePersonOrderActions();
                }
            }
        }, SavePersonOrderAsync);
    }

    private void CancelPersonOrder(object sender, RoutedEventArgs args)
    {
        if (busy) return;
        DiscardPersonOrder();
        if (!CanViewPeople) ShowPage(HomePage);
        else PeopleContent.Visibility = Visibility.Visible;
    }

    private void DiscardPersonOrder()
    {
        orderedPeople = null;
        personOrderRevision = Guid.Empty;
        orderDenied = false;
        PersonOrderList.ItemsSource = null;
        PersonOrderContent.Visibility = PersonOrderNotice.Visibility = PersonOrderRestoredNotice.Visibility = Visibility.Collapsed;
        PersonOrderNotice.IsOpen = false;
        UpdatePersonOrderActions();
    }

    private void ShowPersonOrderNotice(string message, InfoBarSeverity severity = InfoBarSeverity.Error)
    {
        PersonOrderNotice.Message = message;
        PersonOrderNotice.Severity = severity;
        PersonOrderNotice.Visibility = Visibility.Visible;
        PersonOrderNotice.IsOpen = true;
    }
}
