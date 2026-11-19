using System.Net;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Monetail.WinUI.Input;
using Monetail.Windows.Client;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private async Task ExpireReceiptLogin(SyntheticConnection fixture)
    {
        fixture.RejectSession = fixture.RejectAuthentication = true;
        var connection = (ClientConnection)(TransferRuntime("connection") ?? throw new InvalidOperationException("接続がありません。"));
        // 保存APIは未接続。実HTTPの401を共通RunAsyncへ渡し、製品の認証退避入口から検証する。
        Func<Task> action = async () => { await new PeopleWorkspace(connection.Api, connection.Session, ((LedgerInfo)(TransferRuntime("currentLedger") ?? throw new InvalidOperationException("家計簿がありません。"))).LedgerId).ListAsync(); };
        var run = typeof(MainWindow).GetMethod("RunAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("共通操作がありません。");
        await (Task)(run.Invoke(window, [action, (Func<Task>)(() => Task.CompletedTask), false]) ?? throw new InvalidOperationException("操作がありません。"));
    }
    private async Task CompleteReceiptLogin(SyntheticConnection fixture, FrameworkElement root, bool expiresAgain = false)
    {
        fixture.RejectSession = fixture.RejectAuthentication = false; fixture.CompleteLogin = true;
        var browser = fixture.BrowserOpens;
        await InvokeAsync(() => Find<Button>(root, "LoginButton"));
        await Wait(() => fixture.BrowserOpens > browser && Equals(TransferRuntime("busy"), false) && (expiresAgain
            ? Visible(root, "LoginPage")
            : Visible(root, "HomePage") && TransferRuntime("receiptRecoveryPhase")?.ToString() != "Loading"));
        if (fixture.CallbackTask is { } callback) await callback;
    }
    private async Task CheckReceiptReauthentication()
    {
        foreach (var existing in new[] { false, true })
        {
            using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true };
            var server = new ReceiptRecoveryServer(fixture);
            var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync(); await window!.OpenReceiptEditorAsync(existing ? server.Id : null);
            var originalWindow = window ?? throw new InvalidOperationException(); var editor = ReceiptEditing ?? throw new InvalidOperationException("レシート編集がありません。");
            if (!existing)
            {
                Find<ComboBox>(root, "ReceiptCategoryChoice").SelectedIndex = 0;
                await InvokeAsync(() => Find<Button>(root, "ReceiptCategoryAdd"));
            }
            else
            {
                originalWindow.ActivateReceiptCategory(editor.Groups[0].Key);
                Find<ListView>(root, "ReceiptDetailsList").SelectedIndex = 0;
                await Wait(() => editor.SelectedDetailKey is not null);
            }
            var editorModel = ReceiptView.ViewModel;
            var draft = editor.Draft; var parent = editorModel?.ParentInput; var input = TransferRuntime("receiptDetailInput");
            var groupKey = editor.SelectedGroupKey; var detailKey = editor.SelectedDetailKey; var revision = editor.Revision;
            var price = Find<TextBox>(root, "ReceiptItemPrice");
            await ActivateReceiptInputWindowAsync(); await FocusTransferControl(price);
            price.Text = "12x"; price.Select(1, 2);
            var date = Find<DateInputBox>(root, "ReceiptDate"); date.Text = "2026/";
            Find<TimeInputBox>(root, "ReceiptTime").Text = "9:"; Find<TextBox>(root, "ReceiptInterval").Text = "2x";
            Find<TextBox>(root, "ReceiptItemMemo").Text = "未反映メモ";
            var tax = editor.Draft?.Tax; var people = Find<StackPanel>(root, "ReceiptDetailPeople").Children.OfType<CheckBox>().ToArray();
            var scroller = Find<ScrollViewer>(root, "ReceiptInputScroller"); scroller.UpdateLayout();
            scroller.ChangeView(null, Math.Min(150, scroller.ScrollableHeight), null, true); await Settle(); var offset = scroller.VerticalOffset;
            var dirty = editor.NeedsLeaveConfirmation; var reads = server.Details;
            await ExpireReceiptLogin(fixture); await Wait(() => Visible(root, "LoginPage"));
            var context = TransferRuntime("receiptAuthentication");
            Check(ReferenceEquals(editor, ReceiptEditing) && ReferenceEquals(draft, editor.Draft) && ReferenceEquals(editorModel, ReceiptView.ViewModel) && ReferenceEquals(parent, ReceiptView.ViewModel?.ParentInput) &&
                ReferenceEquals(input, TransferRuntime("receiptDetailInput")) && !VisibleAncestors(Find<Grid>(root, "ReceiptEditorPage")), "レシート401は元の編集/親入力/未反映下書き/コントロールを保持して非表示にする");
            await InvokeAsync(() => Find<Button>(root, "LoginButton")); await Wait(() => Visible(root, "BrowserPage"));
            await InvokeAsync(() => Find<Button>(root, "CancelLoginButton")); await Wait(() => Visible(root, "LoginPage") && Equals(TransferRuntime("busy"), false));
            Check(ReferenceEquals(context, TransferRuntime("receiptAuthentication")) && price.Text == "12x" && date.Text == "2026/", "ログイン取消は最初の復帰情報と未確定入力を保持してログイン画面に留まる");
            server.ExpireAt = "taxes";
            await CompleteReceiptLogin(fixture, root, expiresAgain: true);
            Check(ReferenceEquals(context, TransferRuntime("receiptAuthentication")) && ReferenceEquals(editor, ReceiptEditing) && ReferenceEquals(input, TransferRuntime("receiptDetailInput")),
                "税率再取得中の連続401でも最初の編集位置と同じモデル/下書きを再利用する");
            var taxReads = fixture.TaxRatesRequests;
            await CompleteReceiptLogin(fixture, root);
            try { await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), price) && price.SelectionStart == 1 && price.SelectionLength == 2 && Math.Abs(scroller.VerticalOffset - offset) < 1); }
            catch (TaskCanceledException error)
            {
                var view = context?.GetType();
                var positions = view?.GetProperty("Scrolls")?.GetValue(context) as (ScrollViewer Viewer, double X, double Y)[];
                var captured = positions?.FirstOrDefault(position => ReferenceEquals(position.Viewer, scroller));
                throw new InvalidOperationException($"レシート復帰位置: focus={(FocusManager.GetFocusedElement(root.XamlRoot) as FrameworkElement)?.Name}; selection={price.SelectionStart},{price.SelectionLength}; offset={scroller.VerticalOffset}/{offset}; capturedOffset={captured?.Y}; capturedFocus={(view?.GetProperty("Focus")?.GetValue(context) as FrameworkElement)?.Name}; capturedSelection={view?.GetProperty("SelectionStart")?.GetValue(context)},{view?.GetProperty("SelectionLength")?.GetValue(context)}; phase={TransferRuntime("receiptRecoveryPhase")}; loaded={price.IsLoaded}; readonly={price.IsReadOnly}", error);
            }
            Check(ReferenceEquals(originalWindow, window) && ReferenceEquals(editor, ReceiptEditing) && ReferenceEquals(draft, editor.Draft) &&
                ReferenceEquals(price, Find<TextBox>(root, "ReceiptItemPrice")) && people.All(p => Find<StackPanel>(root, "ReceiptDetailPeople").Children.Contains(p)),
                "同じ本人への復帰は元のWindow/モデル/フォーム/人物チェックボックスを再利用する");
            Check(price.Text == "12x" && date.Text == "2026/" && Find<TimeInputBox>(root, "ReceiptTime").Text == "9:" && Find<TextBox>(root, "ReceiptInterval").Text == "2x" &&
                Find<TextBox>(root, "ReceiptItemMemo").Text == "未反映メモ" && editor.Draft?.Tax == tax && editor.Revision == revision &&
                editor.SelectedGroupKey == groupKey && editor.SelectedDetailKey == detailKey && editor.NeedsLeaveConfirmation == dirty,
                "未確定日時/数値/メモ・実税率・旧版・科目/明細選択・未保存判定を復帰で書き換えない");
            Check(fixture.TaxRatesRequests == taxReads + 1 && server.Details == reads && server.Writes == 0 && !price.IsReadOnly &&
                !Find<Button>(root, "ReceiptRegister").IsEnabled && !Find<Button>(root, "ReceiptUpdate").IsEnabled,
                "復帰はマスタ/税率を再取得するだけで詳細GET・自動書込・保存有効化を行わない");
            await PressReceiptKey(0x0D, 0x11); await Settle(); Check(server.Writes == 0, "Ctrl+Enterも復帰後に保存を実行しない");
        }
        await CheckReceiptMasterRetry();
        await CheckReceiptRecoveryIdentity();
        await CheckReceiptRecoveryPermissions();
        await CheckReceiptRecoveryCancellation();
    }
    private async Task CheckReceiptMasterRetry()
    {
        using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true };
        var server = new ReceiptRecoveryServer(fixture);
        var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync(); await window!.OpenReceiptEditorAsync(server.Id);
        var editor = ReceiptEditing ?? throw new InvalidOperationException("編集がありません。");
        window.ActivateReceiptCategory(editor.Groups[0].Key); Find<ListView>(root, "ReceiptDetailsList").SelectedIndex = 0;
        await Wait(() => editor.SelectedDetailKey is not null);
        var price = Find<TextBox>(root, "ReceiptItemPrice"); price.Text = "123x";
        var groupRow = Find<ListView>(root, "ReceiptCategories").Items.OfType<ReceiptCategoryRow>().First();
        var input = TransferRuntime("receiptDetailInput"); var revision = editor.Revision;
        await ExpireReceiptLogin(fixture); server.FailAt = "categories"; await CompleteReceiptLogin(fixture, root);
        Check(price.IsReadOnly && price.IsEnabled && ReferenceEquals(editor, ReceiptEditing) && ReferenceEquals(input, TransferRuntime("receiptDetailInput")) && input is ReceiptDetailInput { HasChanges: true } &&
            Find<InfoBar>(root, "ReceiptRecoveryNotice").Message == "マスタ情報を取得できませんでした" && Find<Button>(root, "ReceiptMasterRetry").Visibility == Visibility.Visible,
            $"マスタ失敗は空データに置き換えず入力をコピー可能なreadonlyで保持し手動再試行を示す: phase={TransferRuntime("receiptRecoveryPhase")}; readonly={price.IsReadOnly}; enabled={price.IsEnabled}; sameEditor={ReferenceEquals(editor, ReceiptEditing)}; sameInput={ReferenceEquals(input, TransferRuntime("receiptDetailInput"))}; notice={Find<InfoBar>(root, "ReceiptRecoveryNotice").Message}; retry={Find<Button>(root, "ReceiptMasterRetry").Visibility}");
        var before = editor.SelectedGroup?.Details.Count;
        global::Windows.ApplicationModel.DataTransfer.Clipboard.Clear();
        await FocusTransferControl(price); price.SelectAll(); await PressReceiptKey(0x43, 0x11); await Settle();
        try { Check(await global::Windows.ApplicationModel.DataTransfer.Clipboard.GetContent().GetTextAsync() == "123x", "readonlyでも入力文字列をCtrl+Cでコピーできる"); }
        finally { global::Windows.ApplicationModel.DataTransfer.Clipboard.Clear(); }
        await PressReceiptKey(0x0D, 0x12); Check(editor.SelectedGroup?.Details.Count == before && price.Text == "123x", "readonlyではAlt+Enterで下書きを反映しない");
        await InvokeAsync(() => Find<Button>(root, "ReceiptBack")); await ConfirmReceipt(root, false);
        Check(ReferenceEquals(editor, ReceiptEditing) && price.Text == "123x", "マスタ失敗中の離脱取消も入力を保持する");
        server.FailAt = null; server.Rate = "1";
        var category = server.Categories.Rows[0]; server.Categories.Rows[0] = category with { Name = "更新後科目" };
        await InvokeAsync(() => Find<Button>(root, "ReceiptMasterRetry")); await Wait(() => TransferRuntime("receiptRecoveryPhase")?.ToString() == "None");
        Check(!price.IsReadOnly && price.Text == "123x" && ReferenceEquals(input, TransferRuntime("receiptDetailInput")) && editor.Revision == revision &&
            ReferenceEquals(groupRow, Find<ListView>(root, "ReceiptCategories").Items[0]) && groupRow.Name == "更新後科目" &&
            Find<TextBlock>(root, "ReceiptItemTaxWarning").Visibility == Visibility.Visible,
            "再試行成功は同じ入力と旧版/実税率を保ち、名称/候補だけ更新して税率不一致を示す");
        var detailId = editor.SelectedGroup?.Details[0].DetailId;
        price.Text = "123"; SelectReceiptTax(Find<ComboBox>(root, "ReceiptItemTax"), 1);
        await InvokeAsync(() => Find<Button>(root, "ReceiptDetailApply"));
        Check(editor.Draft?.Tax == ReceiptTaxSelection.Taxed(1) && editor.SelectedGroup?.Details[0].DetailId == detailId,
            "利用者が明細を反映した後の新規税率も再取得した履歴を使い、既存明細IDは維持する");
        Check(server.Details == 1 && server.Writes == 0, "マスタ再試行でもレシート詳細GET・書込を追加しない");
    }
    private async Task CheckReceiptRecoveryIdentity()
    {
        foreach (var endpoint in new[] { false, true })
        {
            using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true };
            var server = new ReceiptRecoveryServer(fixture);
            var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync(); await window!.OpenReceiptEditorAsync(null);
            Find<TextBox>(root, "ReceiptMemo").Text = "別本人へ渡さない";
            await ExpireReceiptLogin(fixture);
            if (endpoint)
            {
                fixture.RejectSession = fixture.RejectAuthentication = false;
                var connect = typeof(MainWindow).GetMethod("ConnectAsync", BindingFlags.NonPublic | BindingFlags.Instance) ?? throw new InvalidOperationException();
                await (Task)(connect.Invoke(window, [ApiEndpoint.Parse("https://other.invalid")]) ?? throw new InvalidOperationException());
            }
            else { fixture.AccountId = Guid.NewGuid(); await CompleteReceiptLogin(fixture, root); }
            Check(ReceiptEditing is null && TransferRuntime("receiptAuthentication") is null && Find<TextBox>(root, "ReceiptMemo").Text == "" && server.Writes == 0,
                "別本人/接続先では元の編集とコントロール上の入力を破棄して通常のログイン遷移へ進む");
        }
    }
    private async Task CheckReceiptRecoveryPermissions()
    {
        foreach (var role in new[] { "Viewer", "unmapped", "other-owner", "removed", "Editor" })
        {
            using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true };
            var server = new ReceiptRecoveryServer(fixture);
            var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync(); await window!.OpenReceiptEditorAsync(server.Id);
            var editor = ReceiptEditing ?? throw new InvalidOperationException();
            Find<TextBox>(root, "ReceiptMemo").Text = "権限確認後も保持";
            // 元の支払方法が他人物所有の場合、現在値を本人所有へ変えても旧側の権限を省略できない。
            var currentId = role == "Editor" ? server.Payments.ActiveId : server.Payments.OtherActiveId;
            var methodBox = Find<ComboBox>(root, "ReceiptEditorPayment");
            methodBox.SelectedItem = methodBox.Items.OfType<Monetail.Windows.Presentation.Receipts.ReceiptPaymentChoice>().Single(p => p.Id == currentId);
            await ExpireReceiptLogin(fixture);
            server.Payments.Role = role == "Viewer" ? "Viewer" : "Editor";
            if (role == "unmapped") server.Payments.Mapped = false;
            if (role == "other-owner")
            {
                var payment = server.Payments.Rows.First(p => p.PaymentMethodId == editor.SavedPaymentMethodId);
                server.Payments.Rows[server.Payments.Rows.IndexOf(payment)] = payment with { OwnerPersonId = server.Payments.Other };
                var current = server.Payments.Rows.First(p => p.PaymentMethodId == editor.Header.PaymentMethodId);
                server.Payments.Rows[server.Payments.Rows.IndexOf(current)] = current with { OwnerPersonId = server.Payments.Mine };
            }
            fixture.CannotOpenLedger = role == "removed";
            await CompleteReceiptLogin(fixture, root);
            Check(ReferenceEquals(editor, ReceiptEditing) && Find<TextBox>(root, "ReceiptMemo").IsReadOnly == (role != "Editor") &&
                editor.Header.PaymentMethodId == currentId && Find<TextBox>(root, "ReceiptMemo").Text == "権限確認後も保持" && server.Writes == 0 && server.Details == 1 && Find<Button>(root, "ReceiptMasterRetry").Visibility == Visibility.Collapsed,
                "本人所有のEditorは編集再開、Viewer/人物未対応/保存済み所有権喪失/参加解除は入力を保持し書込禁止、詳細GETなし");
        }
    }
    private async Task CheckReceiptRecoveryCancellation()
    {
        using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true };
        var server = new ReceiptRecoveryServer(fixture);
        var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync(); await window!.OpenReceiptEditorAsync(null);
        var original = window; var editor = ReceiptEditing; Find<TextBox>(root, "ReceiptMemo").Text = "終了まで保持";
        await ExpireReceiptLogin(fixture); var context = TransferRuntime("receiptAuthentication");
        var handler = fixture.TaxRatesHandler ?? throw new InvalidOperationException();
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); var entered = false;
        fixture.TaxRatesHandler = async (r, t) => { entered = true; await gate.Task; return await handler(r, t); };
        fixture.RejectSession = fixture.RejectAuthentication = false; fixture.CompleteLogin = true;
        await InvokeAsync(() => Find<Button>(root, "LoginButton")); await Wait(() => entered);
        // 取得中に再度認証退避し、キャンセルを無視した遅延応答が元の画面へ適用されないことを確認。
        var navigate = typeof(MainWindow).GetMethod("NavigateToLogin", BindingFlags.NonPublic | BindingFlags.Instance) ?? throw new InvalidOperationException();
        navigate.Invoke(window, ["再認証が必要です"]); gate.SetResult(true);
        await Wait(() => Equals(TransferRuntime("busy"), false));
        Check(Visible(root, "LoginPage") && ReferenceEquals(context, TransferRuntime("receiptAuthentication")) &&
            ReferenceEquals(editor, ReceiptEditing) && TransferRuntime("receiptTaxRates") is null && Find<TextBox>(root, "ReceiptMemo").Text == "終了まで保持",
            "再退避で取得を中断し、遅延税率応答を適用せず最初の復帰情報/入力を保持する");
        using var next = new SyntheticConnection(); await OpenSyntheticWindow(next);
        object? State(string name) => typeof(MainWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(original);
        Check(State("receiptEditor") is null && State("receiptAuthentication") is null && State("receiptDetailInput") is null && Equals(State("closed"), true) && server.Writes == 0,
            "再認証中の終了は編集・未反映下書き・復帰情報を破棄し、自動保存しない");
    }

    private sealed class ReceiptRecoveryServer
    {
        internal readonly PaymentServer Payments = new();
        internal readonly CategoryServer Categories = new();
        internal readonly Guid Id = Guid.NewGuid(), Revision = Guid.NewGuid();
        internal int Details, Writes;
        internal string? FailAt, ExpireAt;
        internal string Rate = "10";
        internal ReceiptRecoveryServer(SyntheticConnection fixture)
        {
            HttpResponseMessage? Failure(string at)
            {
                if (ExpireAt == at) { ExpireAt = null; fixture.RejectAuthentication = true; return new(HttpStatusCode.Unauthorized); }
                return FailAt == at ? new(HttpStatusCode.ServiceUnavailable) : null;
            }
            fixture.PeopleHandler = (_, _) => Task.FromResult(Failure("people") ?? PaymentServer.Json(Payments.People()));
            fixture.PaymentsHandler = (r, t) => Failure("payments") is { } error ? Task.FromResult(error) : Payments.HandleAsync(r, t);
            fixture.CategoriesHandler = (r, t) => Failure("categories") is { } error ? Task.FromResult(error) : Categories.HandleAsync(r, t);
            fixture.TaxRatesHandler = (_, _) => Task.FromResult(Failure("taxes") ?? PaymentServer.Json(new { rates = new[] {
                new { taxCategoryCode = "Standard", validFrom = "0001-01-01", validTo = (string?)null, rate = Rate }
            } }));
            fixture.ReceiptsHandler = (request, _) =>
            {
                if (request.Method != HttpMethod.Get) { Writes++; throw new InvalidOperationException("保存は未接続"); }
                if (request.RequestUri!.AbsolutePath.EndsWith("/receipts")) return Task.FromResult(PaymentServer.Json(new { receipts = Array.Empty<object>(), nextCursor = (string?)null }));
                Details++;
                return Task.FromResult(PaymentServer.Json(new {
                    receiptId = Id, revision = Revision, transactionAt = DateTime.Today.ToString("yyyy-MM-dd") + "T00:00:00", shopName = "既存", shopLocation = (string?)null,
                    paymentMethodId = Payments.ActiveId, taxInclusionMode = 0, taxRoundingMode = "floor", recurrenceInterval = (int?)null, recurrenceUnit = (string?)null, memo = (string?)null,
                    groups = new[] { new { groupId = Guid.NewGuid(), categoryId = Categories.Rows[0].CategoryId, memo = (string?)null, details = new[] {
                        new { detailId = Guid.NewGuid(), displayOrder = 0, name = "保存済み明細", unitPrice = 100, quantity = 1, unitDiscountAmount = 0, discountRate = "0", taxRate = "10",
                            expenseType = "Variable", occurrenceType = "Irregular", memo = (string?)null, personIds = new[] { Payments.Mine } }
                    } } }
                }));
            };
        }
    }
}
