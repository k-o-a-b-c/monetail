using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.WinUI.Input;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private async Task CheckPeopleFlow(string output, bool capture)
    {
        using (var fixture = new SyntheticConnection(saved: true))
        {
            var server = new PeopleServer();
            fixture.HasPreviousLedger = true;
            fixture.PeopleHandler = server.HandleAsync;
            var root = await OpenSyntheticWindow(fixture);
            await window!.StartAsync();
            Invoke(Find<Button>(root, "SettingsButton"));
            await Wait(() => Find<ListView>(root, "PeopleList").Items.Count == 2);
            var list = Find<ListView>(root, "PeopleList");
            var add = Find<Button>(root, "AddPersonButton");
            var edit = Find<Button>(root, "EditPersonButton");
            Button Save() => Find<Button>(root, "SavePersonButton");
            var name = Find<TextBox>(root, "PersonName");
            var from = Find<DateInputBox>(root, "PersonValidFrom");
            var to = Find<DateInputBox>(root, "PersonValidTo");
            Button Cancel() => Find<Button>(root, "CancelPersonButton");
            Check(add.IsEnabled && !edit.IsEnabled, "管理者は人物追加可能、未選択なら編集不可");
            var reads = server.Reads;
            Invoke(add);
            await Wait(() => Find<ContentDialog>(root, "PersonDialog").IsLoaded && !EditorClosed(root));
            await Wait(() => FitsHorizontally(name, Find<ScrollViewer>(root, "PersonEditorContent")));
            Check(FitsHorizontally(to, Find<ScrollViewer>(root, "PersonEditorContent")), "人物の入力欄がダイアログの表示領域に収まる");
            Check(name.Text == "" && from.Text == "" && to.Text == "" && server.Reads == reads,
                "人物追加は全欄空欄で開き、追加取得しない");
            Check(Find<Grid>(root, "PeopleContent").Visibility == Visibility.Visible &&
                Find<ContentDialog>(root, "PersonDialog").DefaultButton == ContentDialogButton.None,
                "一覧を背後に残すContentDialogで、入力欄のEnterを既定の保存にしない");
            from.Focus(Microsoft.UI.Xaml.FocusState.Keyboard);
            from.Text = "not-a-date";
            Invoke(Cancel());
            await Settle();
            Check(from.Text == "not-a-date" && server.Writes.Count == 0, "キャンセル時に日付補完・保存を行わない");

            Invoke(add);
            await Settle();
            name.Text = new string('あ', 101);
            Invoke(Save());
            await Wait(() => Find<TextBlock>(root, "PersonNameError").Visibility == Visibility.Visible);
            Check(name.Text.Length == 101 && server.Writes.Count == 0, "人物名100文字超のエラーは欄の近くへ表示し入力保持");
            name.Text = "  新しい人物  ";
            from.Text = "261001";
            to.Text = "260930";
            Invoke(Save());
            await Wait(() => Find<TextBlock>(root, "PersonPeriodError").Visibility == Visibility.Visible);
            Check(from.Text == "2026/10/01" && to.Text == "2026/09/30" && server.Writes.Count == 0,
                "期間の前後関係は保存前に確認し入力保持");
            from.Text = "２０２６０４０１";
            to.Text = "不正";
            Invoke(Save());
            await Wait(() => list.Items.Count == 3 && EditorClosed(root));
            using (var body = JsonDocument.Parse(server.Writes[^1].Body))
            {
                Check(body.RootElement.GetProperty("name").GetString() == "新しい人物" &&
                    body.RootElement.GetProperty("validFrom").GetString() == "2026-04-01" &&
                    body.RootElement.GetProperty("validTo").ValueKind == JsonValueKind.Null && server.Reads == reads,
                    "追加は前後空白除去と日付補完を行い、1応答から一覧へ戻る");
            }

            Invoke(add);
            await Settle();
            name.Text = "再試行の人物";
            server.FailNextSave = true;
            Invoke(Save());
            await Wait(() => Find<TextBlock>(root, "PersonSaveError").Visibility == Visibility.Visible);
            Check(name.Text == "再試行の人物" && Find<ScrollViewer>(root, "PersonEditorContent").Visibility == Visibility.Visible,
                "人物追加の通信失敗で入力と編集画面を保持");
            var failed = server.Writes[^1].Body;
            Invoke(Save());
            await Wait(() => list.Items.Count == 4 && EditorClosed(root));
            Check(failed == server.Writes[^1].Body && server.Reads == reads, "人物追加の再送は同じ要求IDと内容を再利用");

            list.SelectedIndex = 0;
            Invoke(edit);
            await Settle();
            Check(name.Text == "父" && server.Reads == reads, "人物編集は一覧の名前・期間・版を使い追加取得しない");
            name.Text = "父・編集中";
            server.ConflictNextSave = true;
            Invoke(Save());
            await Wait(() => Find<Border>(root, "PersonConflict").Visibility == Visibility.Visible);
            Check(name.Text == "父・編集中" && !Save().IsEnabled &&
                Find<TextBlock>(root, "LatestPersonName").Text == "父・最新" &&
                Find<TextBlock>(root, "LatestPersonFrom").Text == "2020/01/01" &&
                Find<TextBlock>(root, "LatestPersonTo").Text == "2030/12/31" && server.Reads == reads,
                "競合は入力と古い版を残し、全最新項目を追加GETなしで表示");
            if (capture) { await Settle(); await Capture(Find<ContentDialog>(root, "PersonDialog"), output, "10-person-conflict"); }
            Invoke(Find<Button>(root, "UseLatestPersonButton"));
            await Settle();
            Check(name.Text == "父・最新" && from.Text == "2020/01/01" && Save().IsEnabled, "明示的な切り替えだけが入力と版を置き換える");
            name.Text = "父・保存後";
            Invoke(Save());
            await Wait(() => EditorClosed(root));
            Check(((PersonRow)list.Items[0]).Name == "父・保存後（本人）" && server.Reads == reads,
                "人物編集は新しい版で保存し、成功応答の一覧を表示");

            list.SelectedIndex = 0;
            Invoke(edit);
            await Settle();
            name.Text = "重複する名前";
            server.DuplicateNextSave = true;
            Invoke(Save());
            await Wait(() => Find<TextBlock>(root, "PersonNameError").Visibility == Visibility.Visible);
            Check(name.Text == "重複する名前" && Find<Border>(root, "PersonConflict").Visibility == Visibility.Collapsed && server.Reads == reads,
                "同名エラーは競合と分けて表示し、入力と通信回数を維持");

            server.Role = "Viewer";
            name.Text = "降格後の未保存入力";
            Invoke(Save());
            await Wait(() => Find<TextBlock>(root, "PersonSaveError").Visibility == Visibility.Visible);
            Check(name.Text == "降格後の未保存入力" && !Save().IsEnabled && server.Reads == reads,
                "権限喪失は同じ画面に入力を残し、再取得も自動遷移もしない");
            Invoke(Cancel());
            await Settle();
            Check(!add.IsEnabled, "権限エラーの応答だけで閉じた後の追加を禁止");
            list.SelectedIndex = 1;
            await Settle();
            Check(!edit.IsEnabled, "降格後は他人の人物編集へ再入場させない");
            list.SelectedIndex = 0;
            Invoke(edit);
            await Settle();
            Check(from.IsReadOnly && to.IsReadOnly && !from.IsTabStop && Save().IsEnabled,
                "降格後も本人の名前は編集可能で期間は読み取り専用");
            name.Text = "本人の名前変更";
            Invoke(Save());
            await Wait(() => EditorClosed(root));
            using (var body = JsonDocument.Parse(server.Writes[^1].Body))
                Check(!body.RootElement.TryGetProperty("validFrom", out _) && !body.RootElement.TryGetProperty("validTo", out _),
                    "本人の名前変更は有効期間の項目を送信しない");

            list.SelectedIndex = 0;
            Invoke(edit);
            await Settle();
            name.Text = "除外後の入力";
            server.Joined = false;
            Invoke(Save());
            await Wait(() => Find<TextBlock>(root, "PersonSaveError").Visibility == Visibility.Visible);
            Check(name.Text == "除外後の入力" && server.Reads == reads, "除外の404でも入力を保持し、一覧を再取得しない");
            Invoke(Cancel());
            await Settle();
            Invoke(Find<Button>(root, "SettingsButton"));
            await Settle();
            Check(!Find<NavigationViewItem>(root, "PeopleNavigation").IsEnabled &&
                Find<StackPanel>(root, "AccountContent").Visibility == Visibility.Visible && server.Reads == reads,
                "未参加の応答を使い、閉じた後は人物管理へ再入場させない");
        }

        using (var fixture = new SyntheticConnection(saved: true))
        {
            var server = new PeopleServer();
            fixture.HasPreviousLedger = true;
            fixture.PeopleHandler = server.HandleAsync;
            var root = await OpenSyntheticWindow(fixture);
            await window!.StartAsync();
            Invoke(Find<Button>(root, "SettingsButton"));
            await Wait(() => Find<ListView>(root, "PeopleList").Items.Count == 2);
            Invoke(Find<Button>(root, "AddPersonButton"));
            await Settle();
            Find<TextBox>(root, "PersonName").Text = "破棄する編集";
            var navigation = Find<NavigationView>(root, "ManagementNavigation");
            Invoke(Find<Button>(root, "ReturnButton"));
            await Settle();
            Check(!EditorClosed(root) && Find<TextBox>(root, "PersonName").Text == "破棄する編集" &&
                Find<Grid>(root, "ManagementPage").Visibility == Visibility.Visible && server.Reads == 1,
                "人物ダイアログを開いている間は背後の移動で入力を破棄しない");
            Invoke(Find<Button>(root, "CancelPersonButton"));
            await Wait(() => EditorClosed(root));
            navigation.SelectedItem = Find<NavigationViewItem>(root, "AccountNavigation");
            await Settle();
            navigation.SelectedItem = Find<NavigationViewItem>(root, "PeopleNavigation");
            await Wait(() => server.Reads == 2);
            Invoke(Find<Button>(root, "AddPersonButton"));
            await Settle();
            Check(Find<TextBox>(root, "PersonName").Text == "" && server.Writes.Count == 0,
                "キャンセル後は通常の画面移動ができ、人物追加は空欄から再開");
            if (capture)
            {
                await Wait(() => FitsHorizontally(Find<TextBox>(root, "PersonName"), Find<ScrollViewer>(root, "PersonEditorContent")));
                await Settle();
                await Capture(Find<ContentDialog>(root, "PersonDialog"), output, "11-person-editor");
            }
        }
    }

    private static bool FitsHorizontally(FrameworkElement element, FrameworkElement viewport)
    {
        var position = element.TransformToVisual(viewport).TransformPoint(new(0, 0));
        return element.ActualWidth > 0 && position.X >= -0.5 && position.X + element.ActualWidth <= viewport.ActualWidth + 0.5;
    }

    private sealed class PeopleServer
    {
        private readonly Guid mine = Guid.NewGuid();
        private readonly List<PersonInfo> people;
        private readonly Dictionary<Guid, Guid> requests = [];
        internal readonly List<(HttpMethod Method, string Body)> Writes = [];
        internal int Reads;
        internal string Role = "Admin";
        internal bool Joined = true, FailNextSave, ConflictNextSave, DuplicateNextSave;

        internal PeopleServer() => people =
        [
            new(Guid.NewGuid(), "母", new(2026, 4, 1), null, 0, Guid.NewGuid()),
            new(mine, "父", null, null, 1, Guid.NewGuid())
        ];

        internal void ChangeOwnPerson()
        {
            var position = people.FindIndex(person => person.PersonId == mine);
            people[position] = people[position] with { Name = "父・認証待ち中の変更", Revision = Guid.NewGuid() };
        }

        internal async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Method == HttpMethod.Get) { Reads++; return Json(Result()); }
            var body = await request.Content!.ReadAsStringAsync(token);
            Writes.Add((request.Method, body));
            if (!Joined) return Denied(HttpStatusCode.NotFound, null, null);
            if (FailNextSave) { FailNextSave = false; return new(HttpStatusCode.ServiceUnavailable); }
            using var document = JsonDocument.Parse(body);
            var input = document.RootElement;
            if (request.Method == HttpMethod.Post)
            {
                if (Role != "Admin") return Denied(HttpStatusCode.Forbidden, Role, mine);
                var id = input.GetProperty("requestId").GetGuid();
                if (!requests.ContainsKey(id))
                {
                    var created = Guid.NewGuid();
                    requests.Add(id, created);
                    people.Add(new(created, input.GetProperty("name").GetString()!, Date(input, "validFrom"), Date(input, "validTo"), people.Count, Guid.NewGuid()));
                }
                return Json(Result());
            }
            var target = Guid.Parse(request.RequestUri!.Segments[^1]);
            if (Role != "Admin" && (target != mine || input.TryGetProperty("validFrom", out _) || input.TryGetProperty("validTo", out _)))
                return Denied(HttpStatusCode.Forbidden, Role, mine);
            var position = people.FindIndex(row => row.PersonId == target);
            if (position < 0) return Denied(HttpStatusCode.NotFound, Role, mine);
            if (ConflictNextSave)
            {
                ConflictNextSave = false;
                people[position] = people[position] with { Name = "父・最新", ValidFrom = new(2020, 1, 1), ValidTo = new(2030, 12, 31), Revision = Guid.NewGuid() };
            }
            if (people[position].Revision != input.GetProperty("revision").GetGuid())
                return Json(new { code = "revision_conflict", latest = people[position] }, HttpStatusCode.Conflict);
            if (DuplicateNextSave)
            {
                DuplicateNextSave = false;
                return Json(new { code = "duplicate_person_name", field = "name" }, HttpStatusCode.Conflict);
            }
            people[position] = people[position] with
            {
                Name = input.GetProperty("name").GetString()!, Revision = Guid.NewGuid(),
                ValidFrom = input.TryGetProperty("validFrom", out _) ? Date(input, "validFrom") : people[position].ValidFrom,
                ValidTo = input.TryGetProperty("validTo", out _) ? Date(input, "validTo") : people[position].ValidTo
            };
            return Json(Result());
        }

        private PeopleInfo Result() => new(people.ToArray(), mine, Role, Guid.NewGuid());
        private static DateOnly? Date(JsonElement input, string name) => input.GetProperty(name).ValueKind == JsonValueKind.Null ? null :
            DateOnly.ParseExact(input.GetProperty(name).GetString()!, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        private static HttpResponseMessage Denied(HttpStatusCode status, string? role, Guid? person) =>
            Json(new { code = "forbidden", permissions = new { currentRole = role, myPersonId = person } }, status);
        private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(value) };
    }
}
