using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Monetail.WinUI.Input;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private static ContentDialog CategoryDialogFor(FrameworkElement root) => Find<ContentDialog>(root, "CategoryDialog");
    private static Button CategoryFooter(FrameworkElement root, bool save) => All(CategoryDialogFor(root)).OfType<Button>()
        .OrderByDescending(b => b.IsLoaded).First(b => Equals(b.Content, save ? CategoryDialogFor(root).PrimaryButtonText : CategoryDialogFor(root).CloseButtonText));
    private static bool CategoryEditorClosed(FrameworkElement root) => Find<ScrollViewer>(root, "CategoryEditorContent").Visibility == Visibility.Collapsed;
    private static async Task CategoryDialogReady(FrameworkElement root) => await Wait(() => !CategoryEditorClosed(root) && CategoryDialogFor(root).IsLoaded &&
        Find<ContentControl>(root, "Operations").IsEnabled && All(CategoryDialogFor(root)).OfType<Button>().Any(b => b.IsLoaded && b.XamlRoot is not null && Equals(b.Content, CategoryDialogFor(root).CloseButtonText)));
    private async Task<FrameworkElement> OpenCategoriesWindow(SyntheticConnection fixture, CategoryServer server)
    {
        fixture.HasPreviousLedger = true; fixture.CategoriesHandler = server.HandleAsync;
        var root = await OpenSyntheticWindow(fixture); await window!.StartAsync();
        Invoke(Find<Button>(root, "SettingsButton"));
        await Wait(() => Find<ListView>(root, "PeopleList").Items.Count == 1);
        Find<NavigationView>(root, "ManagementNavigation").SelectedItem = Find<NavigationViewItem>(root, "CategoriesNavigation");
        await Wait(() => server.Reads == 1 && Find<ListView>(root, "CategoriesList").IsLoaded && Find<ContentControl>(root, "Operations").IsEnabled);
        await Settle(); return root;
    }
    private static async Task OpenCategoryDialog(FrameworkElement root, string action)
    { Invoke(Find<Button>(root, action)); await CategoryDialogReady(root); }
    private static void SelectCategoryRow(FrameworkElement root, Guid id)
    {
        var list = Find<ListView>(root, "CategoriesList"); list.SelectedItem = list.Items.Cast<CategoryRow>().Single(r => r.Category.CategoryId == id);
    }
    private static void ChooseCategory(FrameworkElement root, string field, string code)
    {
        var combo = Find<ComboBox>(root, field); combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().Single(i => Equals(i.Tag, code));
    }
    private static async Task OpenCategoryOrderScreen(FrameworkElement root)
    {
        Invoke(Find<Button>(root, "CategoryOrderButton"));
        await Wait(() => Find<Grid>(root, "CategoryOrderContent").Visibility == Visibility.Visible && Find<Button>(root, "SaveCategoryOrderButton").IsLoaded);
    }
    private async Task CheckCategories()
    {
        // 最小成立ケースを先に確認し、その後に制約・競合・認証復帰へ広げる。
        using (var fixture = new SyntheticConnection(saved: true))
        {
            var server = new CategoryServer(); var root = await OpenCategoriesWindow(fixture, server);
            var list = Find<ListView>(root, "CategoriesList");
            Check(list.Items.Cast<CategoryRow>().Select(r => r.Category.CategoryId).SequenceEqual(server.Rows.Take(2).Select(r => r.CategoryId)), "科目初期表示は本日両端とNULLを含み、APIの収支混在順を維持");
            Check(!Find<Button>(root, "EditCategoryButton").IsEnabled && !Find<Button>(root, "DeleteCategoryButton").IsEnabled, "科目未選択は編集削除不可");
            Check(((CategoryRow)list.Items[0]).IncomeExpense == "支出" && ((CategoryRow)list.Items[1]).IncomeExpense == "収入", "収支区分の日本語表示");
            Find<CheckBox>(root, "IncludeInactiveCategories").IsChecked = true;
            Check(list.Items.Count == 4 && list.Items.Cast<CategoryRow>().Select(r => r.TaxCategory).SequenceEqual(new[] { "標準", "食品軽減", "新聞軽減", "非課税" }) && server.Reads == 1, "税区分4種類を日本語表示し、期間外切替に追加GETなし");
            SelectCategoryRow(root, server.Rows[2].CategoryId); Find<CheckBox>(root, "IncludeInactiveCategories").IsChecked = false;
            Check(list.Items.Count == 2 && list.SelectedItem is null && !Find<Button>(root, "EditCategoryButton").IsEnabled && server.Writes.Count == 0, "フィルターで隠れた選択を解除し、科目を更新削除しない");
            await OpenCategoryDialog(root, "AddCategoryButton");
            Check(Equals(((ComboBoxItem)Find<ComboBox>(root, "CategoryIncome").SelectedItem).Tag, "Expense") && Equals(((ComboBoxItem)Find<ComboBox>(root, "CategoryTax").SelectedItem).Tag, "Standard") && Find<DateInputBox>(root, "CategoryFrom").Text == "" && Find<DateInputBox>(root, "CategoryTo").Text == "", "科目追加の初期値は支出・標準・期間空欄");
            var keyboard = (FormKeyboard)typeof(MainWindow).GetField("categoryKeyboard", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
            Find<TextBox>(root, "CategoryName").Focus(FocusState.Programmatic); await keyboard.HandleEnter(false, false)!;
            Check(ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), Find<ComboBox>(root, "CategoryIncome")) && server.Writes.Count == 0, "科目名のEnterは保存せず収支欄へ移動");
            Find<ComboBox>(root, "CategoryIncome").IsDropDownOpen = true;
            Check(keyboard.HandleEnter(true, false) is null && server.Writes.Count == 0, "科目ComboBox展開中のEnterは標準選択を優先して保存しない");
            Find<ComboBox>(root, "CategoryIncome").IsDropDownOpen = false;
            await Wait(() => FitsHorizontally(Find<DateInputBox>(root, "CategoryTo"), Find<ScrollViewer>(root, "CategoryEditorContent")));
            Check(FitsHorizontally(Find<ComboBox>(root, "CategoryTax"), Find<ScrollViewer>(root, "CategoryEditorContent")), "科目の選択欄と日付欄は標準ダイアログ幅に収まる");
            Find<TextBox>(root, "CategoryName").Text = "  登録  "; Invoke(CategoryFooter(root, true)); await Wait(() => CategoryEditorClosed(root));
            Check(server.Rows.Count == 5 && list.Items.Count == 3 && server.Reads == 1, "科目作成は成功一覧を反映し追加GETなし");
            var created = server.Rows.Single(r => r.Name == "登録"); SelectCategoryRow(root, created.CategoryId);
            await OpenCategoryDialog(root, "DeleteCategoryButton");
            Check(CategoryDialogFor(root).DefaultButton == ContentDialogButton.Close && Find<TextBlock>(root, "CategoryDeleteNotice").Text.Contains("登録"), "科目削除確認は名称表示・キャンセル既定");
            Invoke(CategoryFooter(root, true)); await Wait(() => CategoryEditorClosed(root));
            Check(server.Rows.Count == 4 && server.Reads == 1, "科目削除は応答一覧のみ反映");

            await OpenCategoryDialog(root, "AddCategoryButton");
            var name = Find<TextBox>(root, "CategoryName");
            name.Text = new string('あ', 101); var before = server.Writes.Count;
            Invoke(CategoryFooter(root, true)); await Wait(() => Find<TextBlock>(root, "CategoryNameError").Visibility == Visibility.Visible);
            Check(server.Writes.Count == before && name.Text.Length == 101, "科目名100文字超は送信せず入力保持");
            name.Text = "期間逆転"; Find<DateInputBox>(root, "CategoryFrom").Text = server.Today.AddDays(1).ToString("yyyy/MM/dd"); Find<DateInputBox>(root, "CategoryTo").Text = server.Today.ToString("yyyy/MM/dd");
            Invoke(CategoryFooter(root, true)); await Wait(() => Find<TextBlock>(root, "CategoryPeriodError").Visibility == Visibility.Visible);
            Check(server.Writes.Count == before, "科目期間逆転は送信前に拒否");
            name.Text = "再試行"; Find<DateInputBox>(root, "CategoryFrom").Text = "不正"; Find<DateInputBox>(root, "CategoryTo").Text = "";
            ChooseCategory(root, "CategoryIncome", "Income"); ChooseCategory(root, "CategoryTax", "Exempt");
            server.LoseCreateResponse = true; Invoke(CategoryFooter(root, true)); await Wait(() => Find<TextBlock>(root, "CategorySaveError").Visibility == Visibility.Visible);
            Check(name.Text == "再試行" && Find<DateInputBox>(root, "CategoryFrom").Text == "", "応答喪失は入力保持、不正任意日付は空欄に補完");
            Invoke(CategoryFooter(root, true)); await Wait(() => CategoryEditorClosed(root));
            Check(server.Writes[^1].Body == server.Writes[^2].Body && server.Rows.Count(r => r.Name == "再試行") == 1 && server.Reads == 1, "応答喪失後は同じrequestIdと本文で重複作成を防止");
            created = server.Rows.Single(r => r.Name == "再試行"); SelectCategoryRow(root, created.CategoryId); await OpenCategoryDialog(root, "EditCategoryButton");
            Check(Equals(((ComboBoxItem)Find<ComboBox>(root, "CategoryIncome").SelectedItem).Tag, "Income") && Equals(((ComboBoxItem)Find<ComboBox>(root, "CategoryTax").SelectedItem).Tag, "Exempt"), "編集は追加時の初期値でなく保存済みの収支税区分を表示");
            name.Text = "未保存入力"; ChooseCategory(root, "CategoryIncome", "Expense"); ChooseCategory(root, "CategoryTax", "ReducedNewspaper");
            server.RowConflict = true; Invoke(CategoryFooter(root, true)); await Wait(() => Find<Border>(root, "CategoryConflict").Visibility == Visibility.Visible);
            Check(name.Text == "未保存入力" && !CategoryDialogFor(root).IsPrimaryButtonEnabled &&
                Find<TextBlock>(root, "LatestCategoryName").Text == "最新名" && Find<TextBlock>(root, "LatestCategoryIncome").Text == "収入" && Find<TextBlock>(root, "LatestCategoryTax").Text == "食品軽減" &&
                Find<TextBlock>(root, "LatestCategoryFrom").Text == server.Today.ToString("yyyy/MM/dd") && Find<TextBlock>(root, "LatestCategoryTo").Text == server.Today.ToString("yyyy/MM/dd"), "編集競合は入力保持・保存停止し最新全5項目を提示");
            Invoke(All(CategoryDialogFor(root)).OfType<Button>().Single(b => Equals(b.Content, "最新内容に切り替える")));
            Check(name.Text == "最新名" && CategoryDialogFor(root).IsPrimaryButtonEnabled, "明示操作のみで入力と更新版を最新に切替");
            ChooseCategory(root, "CategoryIncome", "Expense"); ChooseCategory(root, "CategoryTax", "ReducedNewspaper");
            Find<DateInputBox>(root, "CategoryFrom").Text = ""; Find<DateInputBox>(root, "CategoryTo").Text = "";
            Invoke(CategoryFooter(root, true)); await Wait(() => CategoryEditorClosed(root));
            Check(server.Rows.Single(r => r.CategoryId == created.CategoryId) is { IncomeExpense: "Expense", TaxCategoryCode: "ReducedNewspaper", ValidFrom: null, ValidTo: null } && server.Reads == 1, "編集は収支税区分と期間の明示nullを保存し追加GETなし");
            SelectCategoryRow(root, created.CategoryId); await OpenCategoryDialog(root, "DeleteCategoryButton"); server.ReferenceDelete = true;
            Invoke(CategoryFooter(root, true)); await Wait(() => Find<TextBlock>(root, "CategorySaveError").Visibility == Visibility.Visible);
            Check(Find<TextBlock>(root, "CategorySaveError").Text.Contains("参照") && list.Items.Cast<CategoryRow>().Any(r => r.Category.CategoryId == created.CategoryId), "参照中科目は削除拒否し一覧保持");
            Invoke(CategoryFooter(root, false)); await Wait(() => CategoryEditorClosed(root));
            await OpenCategoryDialog(root, "AddCategoryButton"); name.Text = "重複"; server.DuplicateName = true;
            Invoke(CategoryFooter(root, true)); await Wait(() => Find<TextBlock>(root, "CategoryNameError").Visibility == Visibility.Visible);
            Check(name.Text == "重複" && Find<TextBlock>(root, "CategoryNameError").Text.Contains("同じ名前"), "重複名は近傍エラーと入力保持");
            Invoke(CategoryFooter(root, false)); await Wait(() => CategoryEditorClosed(root));

            await OpenCategoryOrderScreen(root); var order = Find<ListView>(root, "CategoryOrderList");
            Check(order.Items.Count == server.Rows.Count && order.Items.Cast<CategoryRow>().Select(r => r.Category.CategoryId).SequenceEqual(server.Rows.Select(r => r.CategoryId)), "科目並び替えは全期間・収支混在の共通順");
            order.SelectedIndex = 0; Invoke(Find<Button>(root, "MoveCategoryDownButton")); server.OrderConflict = true; Invoke(Find<Button>(root, "SaveCategoryOrderButton"));
            await Wait(() => Find<InfoBar>(root, "CategoryOrderNotice").IsOpen);
            Check(((CategoryRow)order.Items[0]).Name == "競合追加" && server.Reads == 1, "科目順序競合は未保存順を破棄し未知の追加行も応答から反映");
            var ids = order.Items.Cast<CategoryRow>().Select(r => r.Category.CategoryId).ToArray();
            Invoke(Find<Button>(root, "SaveCategoryOrderButton")); await Wait(() => Find<Grid>(root, "CategoryOrderContent").Visibility == Visibility.Collapsed);
            using (var body = JsonDocument.Parse(server.Writes[^1].Body)) Check(body.RootElement.GetProperty("categoryIds").EnumerateArray().Select(r => r.GetGuid()).SequenceEqual(ids) && server.Reads == 1, "順序保存は最新全ID・新版を送信し応答一覧へ復帰");
            before = server.Writes.Count; await OpenCategoryOrderScreen(root); order.SelectedIndex = 0; Invoke(Find<Button>(root, "MoveCategoryDownButton")); Invoke(Find<Button>(root, "CancelCategoryOrderButton"));
            Check(server.Writes.Count == before && server.Reads == 1, "並び替えキャンセルは通信なし");
            await OpenCategoryOrderScreen(root); Find<NavigationView>(root, "ManagementNavigation").SelectedItem = Find<NavigationViewItem>(root, "AccountNavigation"); await Settle();
            Check(Find<Grid>(root, "CategoryOrderContent").Visibility == Visibility.Collapsed, "左メニュー移動で未保存科目順を破棄");
        }
        foreach (var role in new[] { "Editor", "Viewer" })
        {
            using var fixture = new SyntheticConnection(saved: true); var server = new CategoryServer { Role = role }; var root = await OpenCategoriesWindow(fixture, server);
            SelectCategoryRow(root, server.Rows[0].CategoryId);
            Check(new[] { "AddCategoryButton", "EditCategoryButton", "DeleteCategoryButton", "CategoryOrderButton" }.All(n => !Find<Button>(root, n).IsEnabled), "科目はEditor/Viewerも閲覧可能だが全変更不可");
        }
        using (var fixture = new SyntheticConnection(saved: true))
        {
            var server = new CategoryServer(); server.Rows.Clear(); var root = await OpenCategoriesWindow(fixture, server);
            Check(Find<TextBlock>(root, "EmptyCategories").Visibility == Visibility.Visible && Find<Button>(root, "AddCategoryButton").IsEnabled, "科目0件でも管理者は追加可能");
            await OpenCategoryOrderScreen(root); Invoke(Find<Button>(root, "SaveCategoryOrderButton")); await Wait(() => Find<Grid>(root, "CategoryOrderContent").Visibility == Visibility.Collapsed);
            using var body = JsonDocument.Parse(server.Writes.Single().Body); Check(body.RootElement.GetProperty("categoryIds").GetArrayLength() == 0, "科目0件の全件並び替えは空配列");
        }
        using (var fixture = new SyntheticConnection(saved: true))
        {
            var server = new CategoryServer(); var root = await OpenCategoriesWindow(fixture, server);
            SelectCategoryRow(root, server.Rows[0].CategoryId); await OpenCategoryDialog(root, "EditCategoryButton");
            Find<TextBox>(root, "CategoryName").Text = "保存不可でも残す"; server.Deny = true; Invoke(CategoryFooter(root, true));
            await Wait(() => Find<TextBlock>(root, "CategorySaveError").Visibility == Visibility.Visible);
            Check(!CategoryDialogFor(root).IsPrimaryButtonEnabled && Find<TextBox>(root, "CategoryName").Text == "保存不可でも残す" && server.Reads == 1, "科目権限喪失は入力保持・保存不可・再取得なし");
            Invoke(CategoryFooter(root, false)); await Wait(() => CategoryEditorClosed(root));
            Check(!Find<Button>(root, "AddCategoryButton").IsEnabled, "権限喪失後も変更不可");
        }
        using (var fixture = new SyntheticConnection(saved: true))
        {
            var server = new CategoryServer(); var root = await OpenCategoriesWindow(fixture, server);
            await OpenCategoryOrderScreen(root); var list = Find<ListView>(root, "CategoryOrderList");
            list.SelectedIndex = 0; Invoke(Find<Button>(root, "MoveCategoryDownButton"));
            var unsaved = list.Items.Cast<CategoryRow>().Select(r => r.Category.CategoryId).ToArray(); server.Deny = true;
            Invoke(Find<Button>(root, "SaveCategoryOrderButton")); await Wait(() => Find<InfoBar>(root, "CategoryOrderNotice").IsOpen);
            Check(!Find<Button>(root, "SaveCategoryOrderButton").IsEnabled && list.Items.Cast<CategoryRow>().Select(r => r.Category.CategoryId).SequenceEqual(unsaved) && server.Reads == 1,
                "科目並び替え中の権限喪失は未保存順を保持して保存停止・追加GETなし");
            Invoke(Find<Button>(root, "CancelCategoryOrderButton"));
        }
        await CheckCategoryRecovery();
    }
    private async Task CheckCategoryRecovery()
    {
        foreach (var mode in new[] { "create", "edit", "order", "other", "demoted" })
        {
            using var fixture = new SyntheticConnection(saved: true); var server = new CategoryServer(); var root = await OpenCategoriesWindow(fixture, server);
            if (mode == "order") { await OpenCategoryOrderScreen(root); Find<ListView>(root, "CategoryOrderList").SelectedIndex = 0; Invoke(Find<Button>(root, "MoveCategoryDownButton")); }
            else
            {
                if (mode == "edit") SelectCategoryRow(root, server.Rows[0].CategoryId);
                await OpenCategoryDialog(root, mode == "edit" ? "EditCategoryButton" : "AddCategoryButton");
                Find<TextBox>(root, "CategoryName").Text = "復元する入力"; ChooseCategory(root, "CategoryIncome", "Income"); ChooseCategory(root, "CategoryTax", "Exempt");
            }
            fixture.RejectSession = true; Invoke(mode == "order" ? Find<Button>(root, "SaveCategoryOrderButton") : CategoryFooter(root, true));
            await Wait(() => Visible(root, "LoginPage") && Find<Button>(root, "LoginButton").IsEnabled);
            var rejected = fixture.RejectedManagementBodies.Single();
            fixture.RejectSession = false; fixture.CompleteLogin = true;
            if (mode == "other") fixture.AccountId = Guid.NewGuid();
            if (mode == "demoted") server.Role = "Viewer";
            Invoke(Find<Button>(root, "LoginButton"));
            if (mode == "other")
            {
                await Wait(() => Visible(root, "HomePage")); await fixture.CallbackTask!;
                Check(CategoryEditorClosed(root) && server.Writes.Count == 0, "科目下書きを別アカウントへ渡さない"); continue;
            }
            await Wait(() => mode == "order" ? Find<Grid>(root, "CategoryOrderContent").Visibility == Visibility.Visible && Find<Button>(root, "SaveCategoryOrderButton").IsEnabled : Find<TextBlock>(root, "CategoryRestoredNotice").Visibility == Visibility.Visible);
            await fixture.CallbackTask!;
            if (mode != "order") await CategoryDialogReady(root);
            Check(server.Writes.Count == 0, "再ログイン復元は自動保存しない");
            if (mode == "demoted")
            {
                Check(!CategoryDialogFor(root).IsPrimaryButtonEnabled && Find<TextBox>(root, "CategoryName").Text == "復元する入力", "再ログイン中の降格は入力保持・保存不可");
                Invoke(CategoryFooter(root, false)); await Wait(() => CategoryEditorClosed(root)); continue;
            }
            if (mode != "order") Check(Equals(((ComboBoxItem)Find<ComboBox>(root, "CategoryTax").SelectedItem).Tag, "Exempt"), "認証復帰は初期選択値で上書きしない");
            Invoke(mode == "order" ? Find<Button>(root, "SaveCategoryOrderButton") : CategoryFooter(root, true));
            await Wait(() => mode == "order" ? Find<Grid>(root, "CategoryOrderContent").Visibility == Visibility.Collapsed : CategoryEditorClosed(root));
            Check(server.Writes.Single().Body == rejected, "認証復帰後もrequestId・入力・行版または全ID・順序版を維持");
        }
    }

    private sealed class CategoryServer
    {
        internal readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);
        internal readonly List<CategoryInfo> Rows = [];
        internal readonly List<(HttpMethod Method, string Body)> Writes = [];
        private readonly Dictionary<Guid, Guid> creates = [];
        private Guid revision = Guid.NewGuid();
        internal int Reads;
        internal string Role = "Admin";
        internal bool LoseCreateResponse, RowConflict, OrderConflict, ReferenceDelete, DuplicateName, Deny;
        internal CategoryServer()
        {
            Rows.Add(new(Guid.NewGuid(), "本日有効", "Expense", "Standard", Today, Today, 0, Guid.NewGuid()));
            Rows.Add(new(Guid.NewGuid(), "無期限", "Income", "ReducedFood", null, null, 1, Guid.NewGuid()));
            Rows.Add(new(Guid.NewGuid(), "終了済み", "Expense", "ReducedNewspaper", null, Today.AddDays(-1), 2, Guid.NewGuid()));
            Rows.Add(new(Guid.NewGuid(), "開始前", "Income", "Exempt", Today.AddDays(1), null, 3, Guid.NewGuid()));
        }
        private object Result(Guid? created = null) => new { categories = Rows.ToArray(), orderRevision = revision, currentRole = Role, categoryId = created };
        private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(value) };
        internal async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Method == HttpMethod.Get) { Reads++; return Json(Result()); }
            var text = await request.Content!.ReadAsStringAsync(token); Writes.Add((request.Method, text));
            using var json = JsonDocument.Parse(text); var body = json.RootElement;
            if (Deny) { Role = "Viewer"; return Json(new { code = "forbidden", permissions = new { currentRole = Role, myPersonId = (Guid?)null } }, HttpStatusCode.Forbidden); }
            if (DuplicateName) { DuplicateName = false; return Json(new { code = "duplicate_category_name", field = "name" }, HttpStatusCode.Conflict); }
            if (request.Method == HttpMethod.Post)
            {
                var requestId = body.GetProperty("requestId").GetGuid();
                if (creates.TryGetValue(requestId, out var prior)) return Json(Result(prior));
                var id = Guid.NewGuid(); creates.Add(requestId, id);
                Rows.Add(new(id, body.GetProperty("name").GetString()!, body.GetProperty("incomeExpense").GetString()!, body.GetProperty("taxCategoryCode").GetString()!, Date(body, "validFrom"), Date(body, "validTo"), Rows.Count, Guid.NewGuid()));
                revision = Guid.NewGuid();
                if (LoseCreateResponse) { LoseCreateResponse = false; throw new HttpRequestException("合成応答喪失"); }
                return Json(Result(id), HttpStatusCode.Created);
            }
            if (request.Method == HttpMethod.Put)
            {
                if (OrderConflict)
                {
                    OrderConflict = false; Rows.Insert(0, new(Guid.NewGuid(), "競合追加", "Income", "Standard", null, null, 0, Guid.NewGuid())); revision = Guid.NewGuid();
                    for (var i = 0; i < Rows.Count; i++) Rows[i] = Rows[i] with { DisplayOrder = i };
                    return Json(new { code = "revision_conflict", latest = Result() }, HttpStatusCode.Conflict);
                }
                if (body.GetProperty("revision").GetGuid() != revision) throw new InvalidOperationException("科目順序版が維持/更新されていません。");
                var ids = body.GetProperty("categoryIds").EnumerateArray().Select(r => r.GetGuid()).ToArray();
                if (ids.Length != Rows.Count || !ids.ToHashSet().SetEquals(Rows.Select(r => r.CategoryId))) throw new InvalidOperationException("科目全IDが必要です。");
                var rows = ids.Select((id, i) => Rows.Single(r => r.CategoryId == id) with { DisplayOrder = i }).ToArray(); Rows.Clear(); Rows.AddRange(rows); revision = Guid.NewGuid(); return Json(Result());
            }
            var key = Guid.Parse(request.RequestUri!.Segments.Last()); var index = Rows.FindIndex(r => r.CategoryId == key); var selected = Rows[index];
            if (RowConflict)
            {
                RowConflict = false; selected = selected with { Name = "最新名", IncomeExpense = "Income", TaxCategoryCode = "ReducedFood", ValidFrom = Today, ValidTo = Today, Revision = Guid.NewGuid() }; Rows[index] = selected;
                return Json(new { code = "revision_conflict", latest = selected }, HttpStatusCode.Conflict);
            }
            if (body.GetProperty("revision").GetGuid() != selected.Revision) throw new InvalidOperationException("科目行版が維持/更新されていません。");
            if (request.Method == HttpMethod.Delete)
            {
                if (ReferenceDelete) return Json(new { code = "category_referenced" }, HttpStatusCode.Conflict);
                Rows.RemoveAt(index); revision = Guid.NewGuid(); return Json(Result());
            }
            Rows[index] = selected with { Name = body.GetProperty("name").GetString()!, IncomeExpense = body.GetProperty("incomeExpense").GetString()!, TaxCategoryCode = body.GetProperty("taxCategoryCode").GetString()!, ValidFrom = Date(body, "validFrom"), ValidTo = Date(body, "validTo"), Revision = Guid.NewGuid() };
            return Json(Result());
        }
        private static DateOnly? Date(JsonElement body, string key) => body.GetProperty(key).ValueKind == JsonValueKind.Null ? null : DateOnly.Parse(body.GetProperty(key).GetString()!);
    }
}
