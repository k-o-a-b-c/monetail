using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Client;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private async Task CheckPersonOrderReauthentication()
    {
        using (var fixture = new SyntheticConnection(saved: true))
        {
            var server = new PeopleServer();
            var root = await RejectPersonOrderSession(fixture, server);
            var rejected = fixture.RejectedPeopleBodies.Single();
            server.ChangeOwnPerson();
            await LoginAgainPersonOrder(fixture, root);
            Check(Find<TextBlock>(root, "PersonOrderRestoredNotice").Visibility == Visibility.Visible &&
                ((PersonRow)Find<ListView>(root, "PersonOrderList").Items[0]).Name == "父・認証待ち中の変更（本人）" &&
                server.Writes.Count == 0 && server.Reads == 2,
                "同じ本人の再ログインで未保存順を復元し、表示名だけ最新へ更新して自動保存しない");
            Check(fixture.Requests.Count(path => path == "/ledgers/open-last") == 1 &&
                fixture.Requests.Count(path => path.EndsWith("/open", StringComparison.Ordinal)) == 1,
                "並び順の復帰も元の家計簿を直接開き、既存の本人・権限確認を共用");
            Invoke(Find<Button>(root, "SavePersonOrderButton"));
            await Wait(() => !OrderVisible(root));
            Check(server.Writes.Single().Body == rejected, "復元後の明示保存も元の全ID順と全体更新版を維持");
        }

        using (var fixture = new SyntheticConnection(saved: true))
        {
            var server = new PeopleServer();
            var root = await RejectPersonOrderSession(fixture, server);
            server.ChangePeopleAndOrder();
            await LoginAgainPersonOrder(fixture, root);
            var list = Find<ListView>(root, "PersonOrderList");
            Check(list.Items.Cast<PersonRow>().Any(row => row.Name == "母") &&
                !list.Items.Cast<PersonRow>().Any(row => row.Name == "子供") && server.Writes.Count == 0,
                "認証待ちに人物が増減しても未保存のID順を黙って補正しない");
            Invoke(Find<Button>(root, "SavePersonOrderButton"));
            await Wait(() => Find<InfoBar>(root, "PersonOrderNotice").IsOpen);
            Check(Find<InfoBar>(root, "PersonOrderNotice").Severity == InfoBarSeverity.Informational &&
                ((PersonRow)list.Items[0]).Name == "子供" &&
                server.Writes.Single().Body == fixture.RejectedPeopleBodies.Single() && server.Reads == 2,
                "復元した古い全体版は保存時に競合し、追加GETなしで最新順へ置き換える");
        }

        foreach (var removed in new[] { false, true })
        {
            using var fixture = new SyntheticConnection(saved: true);
            var server = new PeopleServer();
            var root = await RejectPersonOrderSession(fixture, server);
            using var rejected = JsonDocument.Parse(fixture.RejectedPeopleBodies.Single());
            var ids = rejected.RootElement.GetProperty("personIds").EnumerateArray().Select(value => value.GetGuid()).ToArray();
            if (removed) fixture.CannotOpenLedger = true;
            else server.Role = "Viewer";
            await LoginAgainPersonOrder(fixture, root);
            var list = Find<ListView>(root, "PersonOrderList");
            list.SelectedIndex = 0;
            Check(list.Items.Cast<PersonRow>().Select(row => row.Person.PersonId).SequenceEqual(ids) &&
                !Find<Button>(root, "SavePersonOrderButton").IsEnabled && !Find<Button>(root, "MovePersonDownButton").IsEnabled &&
                server.Writes.Count == 0, removed ? "除外されても未保存順を残すが保存不可" : "降格されても未保存順を残すが保存不可");
            Check(server.Reads == (removed ? 1 : 2), "並び順の認証復帰では未参加の家計簿の人物一覧を取得しない");
        }

        foreach (var changedServer in new[] { false, true })
        {
            using var fixture = new SyntheticConnection(saved: true);
            var server = new PeopleServer();
            var root = await RejectPersonOrderSession(fixture, server);
            fixture.RejectSession = false;
            if (changedServer)
            {
                fixture.Settings.Save(ApiEndpoint.Parse("https://other.example.test/"));
                fixture.Tokens.Current = new("synthetic-access", "synthetic-refresh", DateTimeOffset.UtcNow.AddMinutes(10));
                await window!.StartAsync();
            }
            else
            {
                fixture.AccountId = Guid.NewGuid();
                fixture.CompleteLogin = true;
                Invoke(Find<Button>(root, "LoginButton"));
                await Wait(() => Visible(root, "HomePage"));
                await fixture.CallbackTask!;
            }
            Check(Visible(root, "HomePage") && !OrderVisible(root) &&
                Find<ListView>(root, "PersonOrderList").Items.Count == 0 && server.Writes.Count == 0,
                changedServer ? "別の接続先へ未保存順を引き継がない" : "別のアカウントへ未保存順を引き継がない");
            var requests = changedServer ? fixture.OtherServerRequests : fixture.Requests;
            Check(!requests.Any(path => path.EndsWith("/open", StringComparison.Ordinal)),
                "本人または接続先が違えば元の並び順の家計簿を自動で開かない");
        }
    }

    private async Task<FrameworkElement> RejectPersonOrderSession(SyntheticConnection fixture, PeopleServer server)
    {
        var root = await OpenPersonOrderWindow(fixture, server);
        Find<ListView>(root, "PersonOrderList").SelectedIndex = 1;
        Invoke(Find<Button>(root, "MovePersonUpButton"));
        fixture.RejectSession = true;
        Invoke(Find<Button>(root, "SavePersonOrderButton"));
        await Wait(() => Visible(root, "LoginPage") && Find<Button>(root, "LoginButton").IsEnabled);
        return root;
    }

    private static async Task LoginAgainPersonOrder(SyntheticConnection fixture, FrameworkElement root)
    {
        fixture.RejectSession = false;
        fixture.CompleteLogin = true;
        Invoke(Find<Button>(root, "LoginButton"));
        await Wait(() => OrderVisible(root) && Find<Button>(root, "CancelPersonOrderButton").IsEnabled);
        await fixture.CallbackTask!;
    }
}
