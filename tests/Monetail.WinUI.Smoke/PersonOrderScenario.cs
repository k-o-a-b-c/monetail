using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private async Task CheckPersonOrder()
    {
        using (var fixture = new SyntheticConnection(saved: true))
        {
            var server = new PeopleServer();
            var root = await OpenPersonOrderWindow(fixture, server);
            var list = Find<ListView>(root, "PersonOrderList");
            var up = Find<Button>(root, "MovePersonUpButton");
            var down = Find<Button>(root, "MovePersonDownButton");
            Check(((PersonRow)list.Items[0]).Name == "母" && ((PersonRow)list.Items[1]).Name == "父（本人）" &&
                !up.IsEnabled && !down.IsEnabled && server.Reads == 1,
                "並び順設定は追加GETなしで共通順を表示し、本人を先頭にしない");
            list.SelectedIndex = 0;
            Check(!up.IsEnabled && down.IsEnabled, "共通順の先頭は上へ移動できない");
            Invoke(down);
            Check(list.SelectedIndex == 1 && ((PersonRow)list.Items[1]).Name == "母" && up.IsEnabled && !down.IsEnabled &&
                server.Writes.Count == 0, "上へ・下へは選択を保って移動し、端を越えず通信しない");
            Invoke(Find<Button>(root, "CancelPersonOrderButton"));
            Check(Find<Grid>(root, "PeopleContent").Visibility == Visibility.Visible && server.Writes.Count == 0,
                "並び順のキャンセルは共通順を保存せず一覧へ戻る");
            Invoke(Find<Button>(root, "PersonOrderButton"));
            await Settle();
            Check(((PersonRow)list.Items[0]).Name == "母", "キャンセルした順序を再開せず保存済み共通順を使う");
            list.SelectedIndex = 1;
            Invoke(up);
            var expected = list.Items.Cast<PersonRow>().Select(row => row.Person.PersonId).ToArray();
            server.OrderStarted = new();
            server.ReleaseOrder = new();
            Invoke(Find<Button>(root, "SavePersonOrderButton"));
            await Wait(() => server.OrderStarted.Task.IsCompleted);
            Check(!up.IsEnabled && !down.IsEnabled && !Find<Button>(root, "CancelPersonOrderButton").IsEnabled &&
                !Find<Button>(root, "SavePersonOrderButton").IsEnabled, "並び順保存中は重複・移動・取消を受け付けない");
            server.ReleaseOrder.SetResult();
            await Wait(() => !OrderVisible(root));
            using var body = JsonDocument.Parse(server.Writes.Single().Body);
            Check(server.Writes.Single().Method == HttpMethod.Put &&
                body.RootElement.GetProperty("personIds").EnumerateArray().Select(value => value.GetGuid()).SequenceEqual(expected) &&
                server.Reads == 1 && ((PersonRow)Find<ListView>(root, "PeopleList").Items[0]).Name == "父（本人）",
                "共通順は全IDを1回で保存し、応答だけで本人先頭の通常一覧へ戻る");
        }

        using (var fixture = new SyntheticConnection(saved: true))
        {
            var server = new PeopleServer();
            var root = await OpenPersonOrderWindow(fixture, server);
            var list = Find<ListView>(root, "PersonOrderList");
            list.SelectedIndex = 1;
            Invoke(Find<Button>(root, "MovePersonUpButton"));
            server.ConflictNextOrder = true;
            Invoke(Find<Button>(root, "SavePersonOrderButton"));
            await Wait(() => Find<InfoBar>(root, "PersonOrderNotice").IsOpen);
            Check(OrderVisible(root) && ((PersonRow)list.Items[0]).Name == "子供" && ((PersonRow)list.Items[1]).Name == "父（本人）" &&
                Find<InfoBar>(root, "PersonOrderNotice").Severity == InfoBarSeverity.Informational && server.Reads == 1,
                "共通順の競合は説明して最新一覧へ直接置換し、人物の追加・削除も追加GETなしで反映");
            var stale = server.Writes.Single().Body;
            list.SelectedIndex = 1;
            Invoke(Find<Button>(root, "MovePersonUpButton"));
            Invoke(Find<Button>(root, "SavePersonOrderButton"));
            await Wait(() => !OrderVisible(root));
            using var previous = JsonDocument.Parse(stale);
            using var current = JsonDocument.Parse(server.Writes[^1].Body);
            Check(previous.RootElement.GetProperty("revision").GetGuid() != current.RootElement.GetProperty("revision").GetGuid() &&
                server.Writes.Count == 2 && server.Reads == 1, "競合後の明示保存は新しい全体更新版を使う");
        }

        using (var fixture = new SyntheticConnection(saved: true))
        {
            var server = new PeopleServer();
            var root = await OpenPersonOrderWindow(fixture, server);
            var list = Find<ListView>(root, "PersonOrderList");
            list.SelectedIndex = 1;
            Invoke(Find<Button>(root, "MovePersonUpButton"));
            server.FailNextSave = true;
            Invoke(Find<Button>(root, "SavePersonOrderButton"));
            await Wait(() => Find<InfoBar>(root, "PersonOrderNotice").IsOpen);
            var failed = server.Writes.Single().Body;
            Check(((PersonRow)list.Items[0]).Name == "父（本人）" && server.Reads == 1,
                "並び順の通信失敗は未保存順を保持し再取得しない");
            server.LoseOrderResponse = true;
            Invoke(Find<Button>(root, "SavePersonOrderButton"));
            await Wait(() => server.Writes.Count == 2 && Find<InfoBar>(root, "PersonOrderNotice").IsOpen);
            Check(failed == server.Writes[^1].Body, "並び順の再試行は同じ全IDと全体更新版を使う");
            Invoke(Find<Button>(root, "SavePersonOrderButton"));
            await Wait(() => Find<InfoBar>(root, "PersonOrderNotice").Severity == InfoBarSeverity.Informational);
            Check(((PersonRow)list.Items[0]).Name == "父（本人）" && server.Reads == 1,
                "応答喪失後の再送も更新版競合で確認済みのサーバー順に収束する");
            Invoke(Find<Button>(root, "CancelPersonOrderButton"));
        }

        foreach (var removed in new[] { false, true })
        {
            using var fixture = new SyntheticConnection(saved: true);
            var server = new PeopleServer();
            var root = await OpenPersonOrderWindow(fixture, server);
            var list = Find<ListView>(root, "PersonOrderList");
            list.SelectedIndex = 1;
            Invoke(Find<Button>(root, "MovePersonUpButton"));
            if (removed) server.Joined = false;
            else server.Role = "Viewer";
            Invoke(Find<Button>(root, "SavePersonOrderButton"));
            await Wait(() => Find<InfoBar>(root, "PersonOrderNotice").IsOpen);
            Check(OrderVisible(root) && ((PersonRow)list.Items[0]).Name == "父（本人）" &&
                !Find<Button>(root, "SavePersonOrderButton").IsEnabled && !Find<Button>(root, "MovePersonDownButton").IsEnabled &&
                server.Reads == 1, removed ? "除外後の並び順は保持して保存禁止、再取得や自動遷移はしない" :
                "降格後の並び順は保持して保存禁止、再取得や自動遷移はしない");
            Invoke(Find<Button>(root, "CancelPersonOrderButton"));
            Check(!Find<Button>(root, "PersonOrderButton").IsEnabled &&
                (!removed || Visible(root, "HomePage")), "閉じた後も最新権限で共通順の再入場を止める");
        }

        using (var fixture = new SyntheticConnection(saved: true))
        {
            var server = new PeopleServer();
            var root = await OpenPersonOrderWindow(fixture, server);
            var list = Find<ListView>(root, "PersonOrderList");
            list.SelectedIndex = 1;
            Invoke(Find<Button>(root, "MovePersonUpButton"));
            Find<NavigationView>(root, "ManagementNavigation").SelectedItem = Find<NavigationViewItem>(root, "AccountNavigation");
            await Settle();
            Check(!OrderVisible(root) && server.Writes.Count == 0, "別機能への明示移動は並び順を保存せず破棄");
            Find<NavigationView>(root, "ManagementNavigation").SelectedItem = Find<NavigationViewItem>(root, "PeopleNavigation");
            await Wait(() => server.Reads == 2);
            Invoke(Find<Button>(root, "PersonOrderButton"));
            await Settle();
            window!.AppWindow.ResizeClient(new(900, 620));
            await Settle();
            Check(((PersonRow)list.Items[0]).Name == "母" && list.ActualHeight > 0 &&
                FitsHorizontally(Find<Button>(root, "SavePersonOrderButton"), Find<Grid>(root, "PersonOrderContent")),
                "並び順は一覧から再開し、コンパクトな画面でも一覧と保存操作が収まる");
        }
    }

    private static bool OrderVisible(FrameworkElement root) => Find<Grid>(root, "PersonOrderContent").Visibility == Visibility.Visible;

    private async Task<FrameworkElement> OpenPersonOrderWindow(SyntheticConnection fixture, PeopleServer server)
    {
        var root = await OpenPersonWindow(fixture, server);
        Invoke(Find<Button>(root, "PersonOrderButton"));
        await Settle();
        return root;
    }
}
