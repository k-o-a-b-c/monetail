using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private async Task CheckPersonDeletion()
    {
        using (var fixture = new SyntheticConnection(saved: true))
        {
            var server = new PeopleServer();
            var root = await OpenPersonWindow(fixture, server);
            var list = Find<ListView>(root, "PeopleList");
            var delete = Find<Button>(root, "DeletePersonButton");
            Check(!delete.IsEnabled, "人物未選択では削除できない");
            list.SelectedIndex = 1;
            Check(delete.IsEnabled, "管理者は選択した人物の削除確認を開ける");
            await InvokeAsync(() => delete);
            await Settle();
            var dialog = Find<ContentDialog>(root, "PersonDialog");
            Check(dialog.Title.ToString() == "人物の削除" && dialog.PrimaryButtonText == "削除" &&
                dialog.DefaultButton == ContentDialogButton.Close &&
                Find<TextBlock>(root, "PersonDeleteNotice").Text.Contains("母") &&
                Find<StackPanel>(root, "PersonFields").Visibility == Visibility.Collapsed,
                "人物名を含む削除確認で、既定操作はキャンセル、編集欄は表示しない");
            await InvokeAsync(() => Find<Button>(root, "CancelPersonButton"));
            await Wait(() => EditorClosed(root));
            Check(server.Writes.Count == 0 && server.Reads == 1, "削除のキャンセルは通信を増やさない");

            list.SelectedIndex = 0;
            await InvokeAsync(() => delete);
            await Settle();
            await InvokeAsync(() => Find<Button>(root, "SavePersonButton"));
            await Wait(() => Find<TextBlock>(root, "PersonSaveError").Visibility == Visibility.Visible);
            Check(Find<TextBlock>(root, "PersonSaveError").Text.Contains("参照") && list.Items.Count == 2,
                "参加者と対応する本人も参照が残っていれば削除せず理由を表示");
            await InvokeAsync(() => Find<Button>(root, "CancelPersonButton"));
            await Wait(() => EditorClosed(root));

            list.SelectedIndex = 1;
            await InvokeAsync(() => delete);
            await Settle();
            server.ConflictNextSave = true;
            await InvokeAsync(() => Find<Button>(root, "SavePersonButton"));
            await Wait(() => Find<Border>(root, "PersonConflict").Visibility == Visibility.Visible);
            Check(!Find<Button>(root, "SavePersonButton").IsEnabled &&
                Find<TextBlock>(root, "LatestPersonName").Text == "母・最新" &&
                Find<TextBlock>(root, "LatestPersonFrom").Text == "2020/01/01" &&
                Find<TextBlock>(root, "LatestPersonTo").Text == "2030/12/31" && server.Reads == 1,
                "削除の競合も全最新項目を追加GETなしで表示し、古い版では再削除しない");
            var oldBody = server.Writes[^1].Body;
            await InvokeAsync(() => Find<Button>(root, "UseLatestPersonButton"));
            await Settle();
            Check(Find<TextBlock>(root, "PersonDeleteNotice").Text.Contains("母・最新") &&
                Find<Button>(root, "SavePersonButton").IsEnabled && server.Writes.Count == 2,
                "明示的な切り替えで削除確認を最新の人物名へ更新し、自動削除しない");

            server.DeleteStarted = new();
            server.ReleaseDelete = new();
            await InvokeAsync(() => Find<Button>(root, "SavePersonButton"));
            await Wait(() => server.DeleteStarted.Task.IsCompleted);
            Check(!dialog.IsEnabled && !Find<Button>(root, "SavePersonButton").IsEnabled &&
                !Find<Button>(root, "CancelPersonButton").IsEnabled,
                "削除中は確認・キャンセルを無効にして重複操作を止める");
            server.ReleaseDelete.SetResult();
            await Wait(() => EditorClosed(root));
            using var previous = JsonDocument.Parse(oldBody);
            using var sent = JsonDocument.Parse(server.Writes[^1].Body);
            Check(server.Writes[^1].Method == HttpMethod.Delete &&
                sent.RootElement.GetProperty("revision").GetGuid() != previous.RootElement.GetProperty("revision").GetGuid() &&
                list.Items.Count == 1 && server.Reads == 1,
                "確認した新しい版で削除し、成功応答だけから一覧を更新");
        }

        using (var fixture = new SyntheticConnection(saved: true))
        {
            var server = new PeopleServer();
            var root = await OpenDeletionConfirmation(fixture, server);
            server.FailNextSave = true;
            await InvokeAsync(() => Find<Button>(root, "SavePersonButton"));
            await Wait(() => Find<TextBlock>(root, "PersonSaveError").Visibility == Visibility.Visible);
            var failedBody = server.Writes.Single().Body;
            Check(!EditorClosed(root) && server.Reads == 1, "削除の通信失敗は確認対象を残し再取得しない");
            await InvokeAsync(() => Find<Button>(root, "SavePersonButton"));
            await Wait(() => EditorClosed(root));
            Check(server.Writes[^1].Body == failedBody && Find<ListView>(root, "PeopleList").Items.Count == 1 && server.Reads == 1,
                "削除の再試行は同じ人物と更新版を送り、応答の一覧へ戻る");
        }

        using (var fixture = new SyntheticConnection(saved: true))
        {
            var server = new PeopleServer();
            var root = await OpenDeletionConfirmation(fixture, server);
            server.LoseDeleteResponse = true;
            await InvokeAsync(() => Find<Button>(root, "SavePersonButton"));
            await Wait(() => Find<TextBlock>(root, "PersonSaveError").Visibility == Visibility.Visible);
            Check(Find<ListView>(root, "PeopleList").Items.Count == 2 && server.Reads == 1,
                "削除の応答喪失では一覧から勝手に消さず確認対象を保持");
            await InvokeAsync(() => Find<Button>(root, "SavePersonButton"));
            await Wait(() => !Find<Button>(root, "SavePersonButton").IsEnabled);
            await Wait(() => Find<TextBlock>(root, "PersonSaveError").Text.Contains("削除されている"));
            await InvokeAsync(() => Find<Button>(root, "CancelPersonButton"));
            await Wait(() => EditorClosed(root));
            Check(!Find<Button>(root, "DeletePersonButton").IsEnabled && server.Reads == 1,
                "削除済みの404は再取得せず説明し、閉じた後は同じ対象へ再入場させない");
            await InvokeAsync(() => Find<Button>(root, "SettingsButton"));
            await Wait(() => Find<ListView>(root, "PeopleList").Items.Count == 1);
            Check(server.Reads == 2, "明示的に人物一覧を開き直すとサーバーの削除結果を確認できる");
        }

        foreach (var removed in new[] { false, true })
        {
            using var fixture = new SyntheticConnection(saved: true);
            var server = new PeopleServer();
            var root = await OpenDeletionConfirmation(fixture, server);
            if (removed) server.Joined = false;
            else server.Role = "Viewer";
            await InvokeAsync(() => Find<Button>(root, "SavePersonButton"));
            await Wait(() => Find<TextBlock>(root, "PersonSaveError").Visibility == Visibility.Visible);
            Check(!Find<Button>(root, "SavePersonButton").IsEnabled && server.Reads == 1 && !EditorClosed(root),
                removed ? "除外後の削除は確認内で拒否し追加GETしない" : "降格後の削除は確認内で拒否し追加GETしない");
            await InvokeAsync(() => Find<Button>(root, "CancelPersonButton"));
            await Wait(() => EditorClosed(root));
            Check(!Find<Button>(root, "DeletePersonButton").IsEnabled && !Find<Button>(root, "AddPersonButton").IsEnabled,
                "削除拒否の現在権限を閉じた後のボタンにも反映");
            if (!removed)
            {
                Find<ListView>(root, "PeopleList").SelectedIndex = 0;
                Check(!Find<Button>(root, "DeletePersonButton").IsEnabled && Find<Button>(root, "EditPersonButton").IsEnabled,
                    "降格後も本人の名前編集は保ち、本人の削除は許可しない");
            }
        }

        using (var fixture = new SyntheticConnection(saved: true))
        {
            var server = new PeopleServer();
            var root = await OpenDeletionConfirmation(fixture, server);
            fixture.RejectSession = true;
            await InvokeAsync(() => Find<Button>(root, "SavePersonButton"));
            await Wait(() => Visible(root, "LoginPage") && EditorClosed(root));
            fixture.RejectSession = false;
            fixture.CompleteLogin = true;
            await InvokeAsync(() => Find<Button>(root, "LoginButton"));
            await Wait(() => Visible(root, "HomePage"));
            await fixture.CallbackTask!;
            Check(server.Writes.Count == 0 && EditorClosed(root), "削除中の認証失効は共通ログインへ戻り、再認証後に自動削除しない");
        }
    }

    private async Task<FrameworkElement> OpenDeletionConfirmation(SyntheticConnection fixture, PeopleServer server)
    {
        var root = await OpenPersonWindow(fixture, server);
        Find<ListView>(root, "PeopleList").SelectedIndex = 1;
        await InvokeAsync(() => Find<Button>(root, "DeletePersonButton"));
        await Settle();
        return root;
    }
}
