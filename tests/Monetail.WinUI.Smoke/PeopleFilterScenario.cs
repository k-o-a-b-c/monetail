using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private async Task CheckPeopleFilter()
    {
        using var fixture = new SyntheticConnection(saved: true);
        fixture.HasPreviousLedger = true;
        var today = DateOnly.FromDateTime(DateTime.Now); var mine = Guid.NewGuid(); var orderRevision = Guid.NewGuid();
        PersonInfo[] rows = [new(Guid.NewGuid(), "無期限", null, null, 0, Guid.NewGuid()),
            new(mine, "本人", today, today, 1, Guid.NewGuid()),
            new(Guid.NewGuid(), "終了済み", null, today.AddDays(-1), 2, Guid.NewGuid()),
            new(Guid.NewGuid(), "開始前", today.AddDays(1), null, 3, Guid.NewGuid())];
        var reads = 0; var writes = new List<string>();
        fixture.PeopleHandler = async (request, token) =>
        {
            if (request.Method == HttpMethod.Get) reads++;
            else
            {
                var text = await request.Content!.ReadAsStringAsync(token); writes.Add(text);
                using var body = JsonDocument.Parse(text);
                Check(body.RootElement.GetProperty("revision").GetGuid() == orderRevision, "人物全件並び替えは元の順序版");
                var ids = body.RootElement.GetProperty("personIds").EnumerateArray().Select(r => r.GetGuid()).ToArray();
                Check(ids.Length == rows.Length && ids.ToHashSet().SetEquals(rows.Select(r => r.PersonId)), "人物全件並び替えは期間外も含める");
                rows = ids.Select((id, i) => rows.Single(r => r.PersonId == id) with { DisplayOrder = i }).ToArray(); orderRevision = Guid.NewGuid();
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new PeopleInfo(rows, mine, "Admin", orderRevision)) };
        };
        var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync(); await InvokeAsync(() => Find<Button>(root, "SettingsButton"));
        var list = Find<ListView>(root, "PeopleList"); await Wait(() => list.Items.Count == 2);
        Check(((PersonRow)list.Items[0]).Person.PersonId == mine && ((PersonRow)list.Items[1]).Name == "無期限", "人物初期表示は両端含む本日有効とNULL、本人を先頭");
        var include = Find<CheckBox>(root, "IncludeInactivePeople"); include.IsChecked = true;
        Check(list.Items.Count == 4 && reads == 1, "人物全件表示は取得済み情報だけで切替");
        list.SelectedItem = list.Items.Cast<PersonRow>().Single(r => r.Name == "終了済み"); include.IsChecked = false;
        Check(list.SelectedItem is null && !Find<Button>(root, "EditPersonButton").IsEnabled && writes.Count == 0, "隠れた人物の選択を解除し書込みなし");
        await InvokeAsync(() => Find<Button>(root, "PersonOrderButton")); var order = Find<ListView>(root, "PersonOrderList");
        await Wait(() => order.IsLoaded && order.Items.Count == 4);
        Check(((PersonRow)order.Items[0]).Name == "無期限" && ((PersonRow)order.Items[1]).Person.PersonId == mine, "人物並び替えはフィルターと本人先頭を適用せず全員共通順");
        order.SelectedIndex = 3; await InvokeAsync(() => Find<Button>(root, "MovePersonUpButton")); await InvokeAsync(() => Find<Button>(root, "SavePersonOrderButton"));
        await Wait(() => Find<Grid>(root, "PersonOrderContent").Visibility == Visibility.Collapsed);
        Check(list.Items.Count == 2 && ((PersonRow)list.Items[0]).Person.PersonId == mine && writes.Count == 1 && reads == 1, "人物保存応答も表示フィルター・本人先頭を維持、追加GETなし");
        // 本人自体が期間外でも、人物対応と本人編集権限は全件情報へ保持する。
        rows = rows.Select(r => r.PersonId == mine ? r with { ValidTo = today.AddDays(-1), ValidFrom = null } : r).ToArray();
        Find<NavigationView>(root, "ManagementNavigation").SelectedItem = Find<NavigationViewItem>(root, "AccountNavigation"); await Settle();
        Find<NavigationView>(root, "ManagementNavigation").SelectedItem = Find<NavigationViewItem>(root, "PeopleNavigation");
        await Wait(() => reads == 2 && list.Items.Count == 1);
        Check(((PersonRow)list.Items[0]).Name == "無期限", "期間外本人を特例で初期表示しない"); include.IsChecked = true;
        Check(((PersonRow)list.Items[0]).Person.PersonId == mine && reads == 2 && writes.Count == 1, "再表示した本人は先頭に戻り非表示人物のデータは保持");
    }
}
