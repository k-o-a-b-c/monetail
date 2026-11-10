using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.WinUI.Input;
using Monetail.Windows.Client;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private async Task CheckPersonReauthentication()
    {
        using (var fixture = new SyntheticConnection(saved: true))
        {
            var server = new PeopleServer();
            var root = await OpenPersonWindow(fixture, server);
            await InvokeAsync(() => Find<Button>(root, "AddPersonButton"));
            await Settle();
            Find<TextBox>(root, "PersonName").Text = "  再ログイン後に登録  ";
            Find<DateInputBox>(root, "PersonValidFrom").Text = "20260401";
            fixture.RejectSession = true;
            await WaitPersonEditorReady(root);
            await InvokeAsync(() => Find<Button>(root, "SavePersonButton"));
            await Wait(() => Visible(root, "LoginPage") && EditorClosed(root));
            Check(server.Writes.Count == 0 && fixture.Tokens.Current is null, "人物保存中のログイン失効は保存せずログインへ戻る");
            var rejectedBody = fixture.RejectedPeopleBodies.Single();
            await LoginAgain(fixture, root);
            Check(Find<TextBox>(root, "PersonName").Text == "  再ログイン後に登録  " &&
                Find<DateInputBox>(root, "PersonValidFrom").Text == "2026/04/01" &&
                Find<TextBlock>(root, "PersonRestoredNotice").Visibility == Visibility.Visible && server.Writes.Count == 0,
                "同じアカウントで再ログイン後に入力を復元し、自動保存しない");
            Check(fixture.Requests.Count(path => path == "/ledgers/open-last") == 1 &&
                fixture.Requests.Count(path => path.EndsWith("/open", StringComparison.Ordinal)) == 1 && server.Reads == 3,
                "編集復帰は元の家計簿を直接開き、現在の人物と権限を確認");
            await WaitPersonEditorReady(root);
            await InvokeAsync(() => Find<Button>(root, "SavePersonButton"));
            await Wait(() => EditorClosed(root));
            Check(server.Writes.Single().Body == rejectedBody, "再ログイン後の明示的な登録も元の作成要求IDを維持");
        }

        using (var fixture = new SyntheticConnection(saved: true))
        {
            var server = new PeopleServer();
            var root = await OpenPersonWindow(fixture, server);
            var list = Find<ListView>(root, "PeopleList");
            list.SelectedIndex = 0;
            await InvokeAsync(() => Find<Button>(root, "EditPersonButton"));
            await Settle();
            Find<TextBox>(root, "PersonName").Text = "編集を継続する入力";
            fixture.RejectSession = true;
            await WaitPersonEditorReady(root);
            await InvokeAsync(() => Find<Button>(root, "SavePersonButton"));
            await Wait(() => Visible(root, "LoginPage") && EditorClosed(root));
            server.ChangeOwnPerson();
            await LoginAgain(fixture, root);
            await WaitPersonEditorReady(root);
            await InvokeAsync(() => Find<Button>(root, "SavePersonButton"));
            await Wait(() => Find<Border>(root, "PersonConflict").Visibility == Visibility.Visible);
            Check(Find<TextBox>(root, "PersonName").Text == "編集を継続する入力" && !Find<Button>(root, "SavePersonButton").IsEnabled,
                "編集復帰でも元の更新版を維持し、他人の変更を上書きしない");
        }

        using (var fixture = new SyntheticConnection(saved: true))
        {
            var server = new PeopleServer();
            var root = await OpenPersonWindow(fixture, server);
            await InvokeAsync(() => Find<Button>(root, "AddPersonButton"));
            await Settle();
            Find<TextBox>(root, "PersonName").Text = "別サーバーへ渡さない入力";
            fixture.RejectSession = true;
            await WaitPersonEditorReady(root);
            await InvokeAsync(() => Find<Button>(root, "SavePersonButton"));
            await Wait(() => Visible(root, "LoginPage") && EditorClosed(root));
            fixture.RejectSession = false;
            fixture.Settings.Save(ApiEndpoint.Parse("https://other.example.test/"));
            // 別の起動が変更先でログイン済みの状態。IDが同じでも接続先を区別する。
            fixture.Tokens.Current = new("synthetic-access", "synthetic-refresh", DateTimeOffset.UtcNow.AddMinutes(10));
            await StartSyntheticAsync();
            Check(Visible(root, "HomePage") && Find<TextBox>(root, "PersonName").Text == "" && server.Writes.Count == 0 &&
                !fixture.OtherServerRequests.Any(path => path.EndsWith("/open", StringComparison.Ordinal)),
                "同じアカウントIDでも別のAPI接続先へ入力を引き継がない");
        }

        using (var fixture = new SyntheticConnection(saved: true))
        {
            var server = new PeopleServer();
            var root = await OpenPersonWindow(fixture, server);
            await InvokeAsync(() => Find<Button>(root, "AddPersonButton"));
            await Settle();
            Find<TextBox>(root, "PersonName").Text = "別アカウントへ渡さない入力";
            fixture.RejectSession = true;
            await WaitPersonEditorReady(root);
            await InvokeAsync(() => Find<Button>(root, "SavePersonButton"));
            await Wait(() => Visible(root, "LoginPage") && EditorClosed(root));
            fixture.AccountId = Guid.NewGuid();
            fixture.RejectSession = false;
            fixture.CompleteLogin = true;
            await InvokeAsync(() => Find<Button>(root, "LoginButton"));
            await Wait(() => Visible(root, "HomePage"));
            await fixture.CallbackTask!;
            Check(Find<ScrollViewer>(root, "PersonEditorContent").Visibility == Visibility.Collapsed &&
                Find<TextBox>(root, "PersonName").Text == "" && server.Writes.Count == 0 &&
                !fixture.Requests.Any(path => path.EndsWith("/open", StringComparison.Ordinal)),
                "別アカウントでは入力を引き継がず、元の編集家計簿も自動で開かない");
        }

        foreach (var removed in new[] { false, true })
        {
            using var fixture = new SyntheticConnection(saved: true);
            var server = new PeopleServer();
            var root = await OpenPersonWindow(fixture, server);
            Find<ListView>(root, "PeopleList").SelectedIndex = 0;
            await InvokeAsync(() => Find<Button>(root, "EditPersonButton"));
            await Settle();
            Find<TextBox>(root, "PersonName").Text = "権限が変わっても入力は残す";
            fixture.RejectSession = true;
            await WaitPersonEditorReady(root);
            await InvokeAsync(() => Find<Button>(root, "SavePersonButton"));
            await Wait(() => Visible(root, "LoginPage") && EditorClosed(root));
            if (removed) fixture.CannotOpenLedger = true;
            else server.Role = "Viewer";
            await LoginAgain(fixture, root);
            Check(Find<TextBox>(root, "PersonName").Text == "権限が変わっても入力は残す" &&
                !Find<Button>(root, "SavePersonButton").IsEnabled && server.Writes.Count == 0,
                removed ? "再ログイン後に未参加なら入力だけ残し、保存と最新一覧の取得をしない" :
                    "再ログイン後に期間編集権限がなければ入力を黙って部分保存しない");
            if (removed) Check(server.Reads == 2, "未参加の編集復帰で人物一覧を取得しない");
        }
    }

    private async Task<FrameworkElement> OpenPersonWindow(SyntheticConnection fixture, PeopleServer server)
    {
        fixture.HasPreviousLedger = true;
        fixture.PeopleHandler = server.HandleAsync;
        var root = await OpenSyntheticWindow(fixture);
        await StartSyntheticAsync();
        await InvokeAsync(() => Find<Button>(root, "SettingsButton"));
        await Wait(() => Find<ListView>(root, "PeopleList").Items.Count == 2 && Find<Button>(root, "AddPersonButton").IsLoaded);
        return root;
    }

    private static async Task LoginAgain(SyntheticConnection fixture, FrameworkElement root)
    {
        fixture.RejectSession = false;
        fixture.CompleteLogin = true;
        await InvokeAsync(() => Find<Button>(root, "LoginButton"));
        await Wait(() => Find<ScrollViewer>(root, "PersonEditorContent").Visibility == Visibility.Visible);
        await fixture.CallbackTask!;
        await WaitPersonEditorReady(root);
    }
    private static Task WaitPersonEditorReady(FrameworkElement root) => Wait(() =>
        Find<ContentControl>(root, "Operations").IsEnabled &&
        All(Find<ContentDialog>(root, "PersonDialog")).OfType<Button>().Any(b => b.IsLoaded && b.XamlRoot is not null &&
            Equals(b.Content, Find<ContentDialog>(root, "PersonDialog").PrimaryButtonText)));
}
