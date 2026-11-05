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
    private static ContentDialog PaymentDialogFor(FrameworkElement root) => Find<ContentDialog>(root, "PaymentDialog");
    private static Button PaymentFooter(FrameworkElement root, bool save) => All(PaymentDialogFor(root)).OfType<Button>()
        .First(b => Equals(b.Content, save ? PaymentDialogFor(root).PrimaryButtonText : PaymentDialogFor(root).CloseButtonText));
    private static bool PaymentEditorClosed(FrameworkElement root) => Find<ScrollViewer>(root, "PaymentEditorContent").Visibility == Visibility.Collapsed;
    private static async Task PaymentDialogReady(FrameworkElement root) => await Wait(() => !PaymentEditorClosed(root) &&
        PaymentDialogFor(root).IsLoaded && All(PaymentDialogFor(root)).OfType<Button>().Any(b => Equals(b.Content, PaymentDialogFor(root).CloseButtonText)));
    private async Task<FrameworkElement> OpenPaymentsWindow(SyntheticConnection fixture, PaymentServer server)
    {
        fixture.HasPreviousLedger = true;
        fixture.PeopleHandler = (_, _) => Task.FromResult(PaymentServer.Json(server.People()));
        fixture.PaymentsHandler = server.HandleAsync;
        var root = await OpenSyntheticWindow(fixture); await window!.StartAsync();
        Invoke(Find<Button>(root, "SettingsButton"));
        await Wait(() => Find<ListView>(root, "PeopleList").Items.Count == 3);
        Find<NavigationView>(root, "ManagementNavigation").SelectedItem = Find<NavigationViewItem>(root, "PaymentsNavigation");
        await Wait(() => server.Reads == 1 && Find<ListView>(root, "PaymentsList").Items.Count == 2);
        await Settle(); return root;
    }
    private static async Task OpenPaymentDialog(FrameworkElement root, string button)
    { Invoke(Find<Button>(root, button)); await PaymentDialogReady(root); }
    private static void ChoosePaymentOwner(FrameworkElement root, Guid owner)
    {
        var choice = Find<ComboBox>(root, "PaymentOwnerChoice"); choice.SelectedItem = ((IEnumerable<PersonRow>)choice.ItemsSource).Single(p => p.Person.PersonId == owner);
    }
    private static void SelectPaymentRow(FrameworkElement root, Guid id)
    {
        var list = Find<ListView>(root, "PaymentsList"); list.SelectedItem = list.Items.Cast<PaymentRow>().Single(r => r.Payment.PaymentMethodId == id);
    }
    private static async Task OpenOrderFor(FrameworkElement root, Guid owner)
    {
        var list = Find<ListView>(root, "PaymentsList");
        await Wait(() => All(list).OfType<Button>().Any(b => b.Tag is Guid id && id == owner));
        Invoke(All(list).OfType<Button>().Single(b => b.Tag is Guid id && id == owner));
        await Wait(() => Find<Grid>(root, "PaymentOrderContent").Visibility == Visibility.Visible);
    }
    private async Task CheckPayments()
    {
        using (var fixture = new SyntheticConnection(saved: true))
        {
            var server = new PaymentServer(); var root = await OpenPaymentsWindow(fixture, server);
            var list = Find<ListView>(root, "PaymentsList");
            Check(list.GroupStyle.Count == 1 && list.Items[0] is PaymentRow first && first.Payment.OwnerPersonId == server.Mine,
                "支払方法は標準ListViewで本人グループを先頭に表示");
            Check(list.Items.Cast<PaymentRow>().All(r => r.Payment.PaymentMethodId != server.ExpiredId && r.Payment.ValidFrom != server.Tomorrow),
                "本日両端を含む有効期間で絞り込み、将来開始と終了済みを初期表示から除く");
            Check(!All(list).OfType<TextBlock>().Any(t => t.Text == "空人物"), "表示0件の人物見出しは非表示");
            Find<CheckBox>(root, "IncludeInactivePayments").IsChecked = true;
            Check(list.Items.Count == 4 && server.Reads == 1, "有効期間外表示は保持した全件を使い、追加通信しない");
            Check(!Find<Button>(root, "EditPaymentButton").IsEnabled && !Find<Button>(root, "DeletePaymentButton").IsEnabled, "未選択の編集と削除は無効");
            await OpenPaymentDialog(root, "AddPaymentButton");
            Check(Find<ComboBox>(root, "PaymentOwnerChoice").SelectedIndex == -1 && !PaymentDialogFor(root).IsPrimaryButtonEnabled,
                "管理者の追加所有者は未選択から始まり、選択前は登録不可");
            ChoosePaymentOwner(root, server.Mine);
            Find<TextBox>(root, "PaymentName").Text = new string('あ', 101);
            Invoke(PaymentFooter(root, true));
            await Wait(() => Find<TextBlock>(root, "PaymentNameError").Visibility == Visibility.Visible);
            Check(server.Writes.Count == 0 && !PaymentEditorClosed(root), "100文字超の登録は近傍エラーを表示し、入力とダイアログを維持");
            Find<TextBox>(root, "PaymentName").Text = "  登録  "; Find<DateInputBox>(root, "PaymentFrom").Text = "bad";
            server.FailSave = true; Invoke(PaymentFooter(root, true));
            await Wait(() => Find<TextBlock>(root, "PaymentSaveError").Visibility == Visibility.Visible);
            Check(Find<TextBox>(root, "PaymentName").Text == "  登録  " && Find<DateInputBox>(root, "PaymentFrom").Text == "", "通信失敗は入力を保持し、不正な任意日付は空欄へ補完");
            Invoke(PaymentFooter(root, true)); await Wait(() => PaymentEditorClosed(root));
            Check(server.Writes.Count == 2 && server.Writes[0].Body == server.Writes[1].Body && server.Reads == 1,
                "作成の通信再試行は同じrequestIdと本文を維持し、成功応答だけで一覧を更新");
            SelectPaymentRow(root, server.ActiveId); await OpenPaymentDialog(root, "EditPaymentButton");
            Check(Find<ComboBox>(root, "PaymentOwnerChoice").Visibility == Visibility.Collapsed && Find<TextBlock>(root, "PaymentOwnerName").Text == "父",
                "編集の所有者は読み取り専用の人物名");
            Find<TextBox>(root, "PaymentName").Text = "未保存の名前"; server.RowConflict = true; Invoke(PaymentFooter(root, true));
            await Wait(() => Find<Border>(root, "PaymentConflict").Visibility == Visibility.Visible);
            Check(Find<TextBox>(root, "PaymentName").Text == "未保存の名前" && Find<TextBlock>(root, "LatestPaymentName").Text == "最新名",
                "編集競合は未保存入力を保持し、全最新項目を明示切替まで別表示");
            Invoke(All(PaymentDialogFor(root)).OfType<Button>().Single(b => Equals(b.Content, "最新内容に切り替える")));
            Check(Find<TextBox>(root, "PaymentName").Text == "最新名", "編集競合の最新内容へ明示操作で切替");
            Invoke(PaymentFooter(root, true)); await Wait(() => PaymentEditorClosed(root));
            SelectPaymentRow(root, server.ActiveId); await OpenPaymentDialog(root, "DeletePaymentButton");
            Check(PaymentDialogFor(root).DefaultButton == ContentDialogButton.Close && Find<TextBlock>(root, "PaymentDeleteNotice").Text.Contains("父") && Find<TextBlock>(root, "PaymentDeleteNotice").Text.Contains("最新名"),
                "削除はキャンセル既定の確認で所有者と支払方法名を表示");
            server.ReferenceDelete = true; Invoke(PaymentFooter(root, true)); await Wait(() => Find<TextBlock>(root, "PaymentSaveError").Visibility == Visibility.Visible);
            Check(Find<TextBlock>(root, "PaymentSaveError").Text.Contains("有効終了日") && list.Items.Count > 0, "参照拒否で一覧を維持し終了日による管理を案内");
            Invoke(PaymentFooter(root, false)); await Wait(() => PaymentEditorClosed(root));
            server.ReferenceDelete = false;
            var createdId = server.Rows.Single(r => r.Name == "登録").PaymentMethodId;
            SelectPaymentRow(root, createdId); await OpenPaymentDialog(root, "DeletePaymentButton");
            Invoke(PaymentFooter(root, true)); await Wait(() => PaymentEditorClosed(root));
            Check(!list.Items.Cast<PaymentRow>().Any(r => r.Payment.PaymentMethodId == createdId) && server.Reads == 1,
                "未使用の削除成功は応答だけで一覧へ反映");
            var reads = server.Reads;
            await OpenOrderFor(root, server.Mine);
            var order = Find<ListView>(root, "PaymentOrderList");
            Check(order.Items.Cast<PaymentRow>().Any(r => r.Payment.PaymentMethodId == server.ExpiredId) && Find<TextBlock>(root, "PaymentOrderOwner").Text == "父",
                "人物別専用画面は対象名を固定し、全期間の支払方法を表示");
            order.SelectedIndex = 0; Check(!Find<Button>(root, "MovePaymentUpButton").IsEnabled, "先頭は上へ移動不可");
            Invoke(Find<Button>(root, "MovePaymentDownButton")); server.OrderConflict = true; Invoke(Find<Button>(root, "SavePaymentOrderButton"));
            await Wait(() => Find<InfoBar>(root, "PaymentOrderNotice").IsOpen);
            Check(order.Items.Cast<PaymentRow>().First().Name == "競合時に追加された方法" && server.Reads == reads,
                "並び順競合は未知の追加行も応答の全情報で表示し、未保存順を破棄、追加GETなし");
            var currentIds = order.Items.Cast<PaymentRow>().Select(r => r.Payment.PaymentMethodId).ToArray();
            Invoke(Find<Button>(root, "SavePaymentOrderButton")); await Wait(() => Find<Grid>(root, "PaymentOrderContent").Visibility == Visibility.Collapsed);
            using var savedOrder = JsonDocument.Parse(server.Writes.Last().Body);
            Check(savedOrder.RootElement.GetProperty("ownerPersonId").GetGuid() == server.Mine && savedOrder.RootElement.GetProperty("paymentMethodIds").EnumerateArray().Select(p => p.GetGuid()).SequenceEqual(currentIds),
                "競合後の保存は対象人物と新版・最新全IDを1要求で送信");
            var writes = server.Writes.Count;
            await OpenOrderFor(root, server.Mine); Find<ListView>(root, "PaymentOrderList").SelectedIndex = 0;
            Invoke(Find<Button>(root, "MovePaymentDownButton")); Invoke(Find<Button>(root, "CancelPaymentOrderButton"));
            Check(server.Reads == reads && server.Writes.Count == writes, "並び順キャンセルでは保存も追加GETもしない");
            await OpenOrderFor(root, server.Mine);
            Find<NavigationView>(root, "ManagementNavigation").SelectedItem = Find<NavigationViewItem>(root, "AccountNavigation");
            await Settle(); Check(Find<Grid>(root, "PaymentOrderContent").Visibility == Visibility.Collapsed, "左メニュー移動時は未保存順を破棄");
        }
        foreach (var role in new[] { "Editor", "Viewer" })
        {
            using var fixture = new SyntheticConnection(saved: true); var server = new PaymentServer { Role = role };
            var root = await OpenPaymentsWindow(fixture, server); var list = Find<ListView>(root, "PaymentsList");
            Check(Find<Button>(root, "AddPaymentButton").IsEnabled == (role == "Editor"), "追加可否はEditor本人対応とViewerを区別");
            var buttons = All(list).OfType<Button>().Where(b => Equals(b.Content, "並び順変更") && b.Visibility == Visibility.Visible).ToArray();
            Check(role == "Viewer" ? buttons.Length == 0 : buttons.All(b => b.Tag is Guid id && id == server.Mine), "権限のない人物の並び順ボタンを非表示");
            SelectPaymentRow(root, server.OtherActiveId); Check(!Find<Button>(root, "EditPaymentButton").IsEnabled, "Editorも他人物の編集不可");
            if (role == "Editor")
            {
                await OpenPaymentDialog(root, "AddPaymentButton");
                Check(Find<ComboBox>(root, "PaymentOwnerChoice").Visibility == Visibility.Collapsed && Find<TextBlock>(root, "PaymentOwnerName").Text == "父", "Editor追加の所有者は本人に固定");
                Invoke(PaymentFooter(root, false)); await Wait(() => PaymentEditorClosed(root));
                SelectPaymentRow(root, server.ActiveId); await OpenPaymentDialog(root, "EditPaymentButton");
                Find<TextBox>(root, "PaymentName").Text = "拒否後も残す"; server.Deny = true; Invoke(PaymentFooter(root, true));
                await Wait(() => Find<TextBlock>(root, "PaymentSaveError").Visibility == Visibility.Visible);
                Check(!PaymentDialogFor(root).IsPrimaryButtonEnabled && Find<TextBox>(root, "PaymentName").Text == "拒否後も残す", "権限喪失は最新権限で保存不可とし入力維持");
                Invoke(PaymentFooter(root, false)); await Wait(() => PaymentEditorClosed(root));
                Check(!Find<Button>(root, "AddPaymentButton").IsEnabled && !Find<Button>(root, "EditPaymentButton").IsEnabled, "権限喪失後はダイアログを閉じても変更操作不可");
            }
        }
        using (var fixture = new SyntheticConnection(saved: true))
        {
            var server = new PaymentServer { Role = "Editor", Mapped = false }; var root = await OpenPaymentsWindow(fixture, server);
            Check(!Find<Button>(root, "AddPaymentButton").IsEnabled && ((PaymentRow)Find<ListView>(root, "PaymentsList").Items[0]).Payment.OwnerPersonId == server.Other,
                "人物未対応Editorは追加不可、本人優先なしで人物共通順を表示");
        }
        await CheckPaymentRecovery();
    }
    private async Task CheckPaymentRecovery()
    {
        foreach (var order in new[] { false, true })
        {
            using var fixture = new SyntheticConnection(saved: true); var server = new PaymentServer();
            var root = await OpenPaymentsWindow(fixture, server);
            if (order)
            {
                await OpenOrderFor(root, server.Mine); Find<ListView>(root, "PaymentOrderList").SelectedIndex = 1;
                Invoke(Find<Button>(root, "MovePaymentUpButton"));
            }
            else
            {
                await OpenPaymentDialog(root, "AddPaymentButton"); ChoosePaymentOwner(root, server.Mine);
                Find<TextBox>(root, "PaymentName").Text = "再ログインで復元";
            }
            fixture.RejectSession = true;
            Invoke(order ? Find<Button>(root, "SavePaymentOrderButton") : PaymentFooter(root, true));
            await Wait(() => Visible(root, "LoginPage") && Find<Button>(root, "LoginButton").IsEnabled);
            var rejected = fixture.RejectedManagementBodies.Single();
            fixture.RejectSession = false; fixture.CompleteLogin = true; Invoke(Find<Button>(root, "LoginButton"));
            await Wait(() => order ? Find<Grid>(root, "PaymentOrderContent").Visibility == Visibility.Visible && Find<Button>(root, "SavePaymentOrderButton").IsEnabled :
                !PaymentEditorClosed(root) && PaymentDialogFor(root).IsPrimaryButtonEnabled);
            await fixture.CallbackTask!;
            Check(server.Writes.Count == 0, "支払方法の同一アカウント再ログインは入力/未保存順を復元し、自動保存しない");
            if (!order) await PaymentDialogReady(root);
            Invoke(order ? Find<Button>(root, "SavePaymentOrderButton") : PaymentFooter(root, true));
            await Wait(() => order ? Find<Grid>(root, "PaymentOrderContent").Visibility == Visibility.Collapsed : PaymentEditorClosed(root));
            Check(server.Writes.Single().Body == rejected, "再ログイン後も作成requestIdまたは旧順序版と全IDを維持");
        }
        using (var fixture = new SyntheticConnection(saved: true))
        {
            var server = new PaymentServer(); var root = await OpenPaymentsWindow(fixture, server);
            await OpenPaymentDialog(root, "AddPaymentButton"); ChoosePaymentOwner(root, server.Other); Find<TextBox>(root, "PaymentName").Text = "元の所有者を維持";
            fixture.RejectSession = true; Invoke(PaymentFooter(root, true));
            await Wait(() => Visible(root, "LoginPage") && Find<Button>(root, "LoginButton").IsEnabled);
            server.Role = "Editor"; fixture.RejectSession = false; fixture.CompleteLogin = true; Invoke(Find<Button>(root, "LoginButton"));
            await Wait(() => Find<TextBlock>(root, "PaymentRestoredNotice").Visibility == Visibility.Visible); await fixture.CallbackTask!; await PaymentDialogReady(root);
            Check(!PaymentDialogFor(root).IsPrimaryButtonEnabled && Find<TextBlock>(root, "PaymentOwnerName").Text == "母" && Find<TextBox>(root, "PaymentName").Text == "元の所有者を維持",
                "認証待ち中に降格した場合は元の所有者と入力を保持し、本人へ割り替えず保存不可");
            Invoke(PaymentFooter(root, false)); await Wait(() => PaymentEditorClosed(root));
        }
        using (var fixture = new SyntheticConnection(saved: true))
        {
            var server = new PaymentServer(); var root = await OpenPaymentsWindow(fixture, server);
            await OpenPaymentDialog(root, "AddPaymentButton"); ChoosePaymentOwner(root, server.Mine); Find<TextBox>(root, "PaymentName").Text = "別人には渡さない";
            fixture.RejectSession = true; Invoke(PaymentFooter(root, true));
            await Wait(() => Visible(root, "LoginPage") && Find<Button>(root, "LoginButton").IsEnabled);
            fixture.RejectSession = false; fixture.CompleteLogin = true; fixture.AccountId = Guid.NewGuid(); Invoke(Find<Button>(root, "LoginButton"));
            await Wait(() => Visible(root, "HomePage")); await fixture.CallbackTask!;
            Check(PaymentEditorClosed(root) && server.Writes.Count == 0, "別アカウントに支払方法の下書きを復元しない");
        }
    }

    private sealed class PaymentServer
    {
        internal readonly Guid Mine = Guid.NewGuid(), Other = Guid.NewGuid(), Empty = Guid.NewGuid();
        internal readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);
        internal DateOnly Tomorrow => Today.AddDays(1);
        internal readonly List<PaymentMethodInfo> Rows = [];
        private readonly Dictionary<Guid, Guid> orders = [], creates = [];
        internal readonly List<(HttpMethod Method, string Body)> Writes = [];
        internal int Reads;
        internal string Role = "Admin";
        internal bool FailSave, RowConflict, ReferenceDelete, OrderConflict, Deny;
        internal bool Mapped = true;
        internal Guid ActiveId => Rows.Single(r => r.Name is "本日有効" or "最新名").PaymentMethodId;
        internal Guid OtherActiveId => Rows.Single(r => r.Name == "母の現金").PaymentMethodId;
        internal Guid ExpiredId => Rows.Single(r => r.Name == "終了済み").PaymentMethodId;
        internal PaymentServer()
        {
            foreach (var owner in new[] { Other, Mine, Empty }) orders.Add(owner, Guid.NewGuid());
            Rows.Add(new(Guid.NewGuid(), Mine, "本日有効", 0, Today, Today, Guid.NewGuid()));
            Rows.Add(new(Guid.NewGuid(), Mine, "終了済み", 1, null, Today.AddDays(-1), Guid.NewGuid()));
            Rows.Add(new(Guid.NewGuid(), Other, "母の現金", 0, null, null, Guid.NewGuid()));
            Rows.Add(new(Guid.NewGuid(), Other, "開始予定", 1, Tomorrow, null, Guid.NewGuid()));
        }
        internal PeopleInfo People() => new([new(Other, "母", null, null, 0, Guid.NewGuid()), new(Mine, "父", null, null, 1, Guid.NewGuid()), new(Empty, "空人物", null, null, 2, Guid.NewGuid())], Mapped ? Mine : null, Role, Guid.NewGuid());
        private object Result(Guid? created = null) => new {
            paymentMethods = Rows.OrderBy(r => r.OwnerPersonId.ToString("N"), StringComparer.Ordinal).ThenBy(r => r.DisplayOrder).ThenBy(r => r.PaymentMethodId.ToString("N"), StringComparer.Ordinal).ToArray(),
            ownerOrderRevisions = new[] { Other, Mine, Empty }.Select(p => new PaymentOwnerOrderInfo(p, orders[p])).ToArray(), myPersonId = Mapped ? (Guid?)Mine : null, currentRole = Role, paymentMethodId = created
        };
        internal static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(value) };
        internal async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Method == HttpMethod.Get) { Reads++; return Json(Result()); }
            var text = await request.Content!.ReadAsStringAsync(token); Writes.Add((request.Method, text));
            using var json = JsonDocument.Parse(text); var body = json.RootElement;
            if (Deny) { Deny = false; Role = "Viewer"; return Json(new { code = "forbidden", permissions = new { currentRole = Role, myPersonId = Mine } }, HttpStatusCode.Forbidden); }
            if (FailSave) { FailSave = false; return new(HttpStatusCode.ServiceUnavailable); }
            if (request.Method == HttpMethod.Post)
            {
                var owner = body.GetProperty("ownerPersonId").GetGuid(); var requestId = body.GetProperty("requestId").GetGuid();
                if (creates.TryGetValue(requestId, out var existing)) return Json(Result(existing));
                var id = Guid.NewGuid(); creates.Add(requestId, id);
                Rows.Add(new(id, owner, body.GetProperty("name").GetString()!, Rows.Count(r => r.OwnerPersonId == owner), Date(body, "validFrom"), Date(body, "validTo"), Guid.NewGuid()));
                orders[owner] = Guid.NewGuid(); return Json(Result(id), HttpStatusCode.Created);
            }
            if (request.Method == HttpMethod.Put)
            {
                var owner = body.GetProperty("ownerPersonId").GetGuid();
                if (OrderConflict)
                {
                    OrderConflict = false;
                    Rows.Add(new(Guid.NewGuid(), owner, "競合時に追加された方法", 0, Today, Today, Guid.NewGuid()));
                    var i = 1;
                    foreach (var row in Rows.Where(r => r.OwnerPersonId == owner && r.Name != "競合時に追加された方法").ToArray()) Rows[Rows.IndexOf(row)] = row with { DisplayOrder = i++ };
                    orders[owner] = Guid.NewGuid();
                    var latest = Rows.Where(r => r.OwnerPersonId == owner).OrderBy(r => r.DisplayOrder).ToArray();
                    return Json(new { code = "revision_conflict", latest = new PaymentMethodOrderInfo(owner, orders[owner], latest.Select(r => r.PaymentMethodId).ToArray(), latest) }, HttpStatusCode.Conflict);
                }
                if (body.GetProperty("revision").GetGuid() != orders[owner]) throw new InvalidOperationException("順序版が復元/更新されていません。");
                var ids = body.GetProperty("paymentMethodIds").EnumerateArray().Select(p => p.GetGuid()).ToArray();
                if (!ids.ToHashSet().SetEquals(Rows.Where(r => r.OwnerPersonId == owner).Select(r => r.PaymentMethodId))) throw new InvalidOperationException("全IDが必要です。");
                for (var i = 0; i < ids.Length; i++) { var index = Rows.FindIndex(r => r.PaymentMethodId == ids[i]); Rows[index] = Rows[index] with { DisplayOrder = i }; }
                orders[owner] = Guid.NewGuid(); return Json(Result());
            }
            var key = Guid.Parse(request.RequestUri!.Segments.Last()); var indexOf = Rows.FindIndex(r => r.PaymentMethodId == key); var selected = Rows[indexOf];
            if (RowConflict)
            {
                RowConflict = false; selected = selected with { Name = "最新名", Revision = Guid.NewGuid() }; Rows[indexOf] = selected;
                return Json(new { code = "revision_conflict", latest = selected }, HttpStatusCode.Conflict);
            }
            if (body.GetProperty("revision").GetGuid() != selected.Revision) throw new InvalidOperationException("行更新版が最新に切り替わっていません。");
            if (request.Method == HttpMethod.Delete)
            {
                if (ReferenceDelete) return Json(new { code = "payment_method_referenced" }, HttpStatusCode.Conflict);
                Rows.RemoveAt(indexOf); orders[selected.OwnerPersonId] = Guid.NewGuid(); return Json(Result());
            }
            Rows[indexOf] = selected with { Name = body.GetProperty("name").GetString()!, ValidFrom = Date(body, "validFrom"), ValidTo = Date(body, "validTo"), Revision = Guid.NewGuid() };
            return Json(Result());
        }
        private static DateOnly? Date(JsonElement body, string key) => body.GetProperty(key).ValueKind == JsonValueKind.Null ? null : DateOnly.Parse(body.GetProperty(key).GetString()!);
    }
}
