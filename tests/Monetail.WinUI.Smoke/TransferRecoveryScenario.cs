using System.Net;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Monetail.WinUI.Input;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transfers;
using Monetail.Windows.Client.Transport;
using Windows.Graphics;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private object? TransferRuntime(string name) => typeof(MainWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window);
    private async Task CompleteTransferLogin(SyntheticConnection fixture, FrameworkElement root, bool mastersExpireAgain = false)
    {
        fixture.RejectSession = fixture.RejectAuthentication = false; fixture.CompleteLogin = true;
        var browserOpens = fixture.BrowserOpens;
        await InvokeAsync(() => Find<Button>(root, "LoginButton"));
        await Wait(() => fixture.BrowserOpens > browserOpens && Equals(TransferRuntime("busy"), false) && (mastersExpireAgain
            ? Visible(root, "LoginPage")
            : Visible(root, "HomePage") && Equals(TransferRuntime("transferMastersLoading"), false)));
        if (fixture.CallbackTask is { } callback) await callback;
    }
    private async Task SubmitTransferToExpiredLogin(SyntheticConnection fixture, TransferInputServer server, FrameworkElement root, string operation)
    {
        fixture.RejectAuthentication = true; server.WriteError = HttpStatusCode.Unauthorized;
        await InvokeAsync(() => Find<Button>(root, operation == "delete" ? "TransferDelete" : operation == "reuse" ? "TransferReuse" : "TransferSave"));
        if (operation != "new") await ConfirmTransfer(root, ContentDialogResult.Primary);
        await Wait(() => Visible(root, "LoginPage"));
    }

    private async Task CheckTransferReauthentication()
    {
        await CheckNavigationLifetime();
        foreach (var operation in new[] { "new", "update", "reuse", "delete" })
        {
            using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true };
            var masters = new PaymentServer(); var server = new TransferInputServer(masters); var peopleReads = 0; var expirePeople = false;
            fixture.PeopleHandler = (_, _) =>
            {
                peopleReads++;
                if (expirePeople) { expirePeople = false; fixture.RejectAuthentication = true; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)); }
                return Task.FromResult(PaymentServer.Json(masters.People()));
            };
            fixture.PaymentsHandler = masters.HandleAsync; fixture.TransfersHandler = server.HandleAsync;
            TransferInfo? saved = operation == "new" ? null : server.Add();
            var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync();
            var originalWindow = window ?? throw new InvalidOperationException("振替Windowがありません。"); await originalWindow.OpenTransferEditorAsync(saved?.TransferId);
            if (operation == "new") await FillTransfer(root, masters);
            else { await FocusTransferControl(Find<TextBox>(root, "TransferAmount")); Find<TextBox>(root, "TransferAmount").Text = "2000"; await Wait(() => TransferEditing?.Draft.Amount == 2000); }
            var editor = TransferEditing ?? throw new InvalidOperationException("編集状態がありません。"); var draft = editor.Draft;
            var controls = new Control[] { Find<DateInputBox>(root, "TransferDate"), Find<TimeInputBox>(root, "TransferTime"), Find<AutoSuggestBox>(root, "TransferName"), Find<ComboBox>(root, "TransferFrom"), Find<ComboBox>(root, "TransferTo"), Find<TextBox>(root, "TransferAmount"), Find<TextBox>(root, "TransferMemo") };
            var from = draft.From; var to = draft.To; var revision = editor.Revision; var amount = draft.Amount;
            await SubmitTransferToExpiredLogin(fixture, server, root, operation);
            var pending = TransferRuntime("transferPending") as TransferWriteRequest ?? throw new InvalidOperationException("元要求がありません。");
            var reads = server.Reads.Count; var beforePeople = peopleReads; var beforePayments = masters.Reads;
            Check(ReferenceEquals(window, originalWindow) && ReferenceEquals(editor, TransferEditing) && ReferenceEquals(draft, TransferEditing?.Draft) && !VisibleAncestors(controls[0]) && server.Writes.Count == 1,
                $"振替{operation}の401はWindow・モデル・Draftを破棄せず入力画面を隠す");
            Check(Equals(TransferRuntime("transferUnknown"), false) && editor.Revision == revision && draft.From == from && draft.To == to && draft.Amount == amount,
                "確定401を結果不明にせず入力・選択由来・旧revisionと要求を保持する");
            if (operation == "new")
            {
                await InvokeAsync(() => Find<Button>(root, "LoginButton")); await Wait(() => Visible(root, "BrowserPage"));
                await InvokeAsync(() => Find<Button>(root, "CancelLoginButton")); await Wait(() => Visible(root, "LoginPage") && Equals(TransferRuntime("busy"), false));
                Check(ReferenceEquals(editor, TransferEditing) && ReferenceEquals(pending, TransferRuntime("transferPending")) && !VisibleAncestors(controls[0]), "ログイン取消後もログイン画面に留まり編集と元要求を保持する");
            }
            server.WriteError = null;
            var capturedView = TransferRuntime("transferAuthentication"); expirePeople = true;
            await CompleteTransferLogin(fixture, root, mastersExpireAgain: true);
            Check(capturedView is not null && ReferenceEquals(capturedView, TransferRuntime("transferAuthentication")) && ReferenceEquals(pending, TransferRuntime("transferPending")) &&
                ReferenceEquals(editor, TransferEditing) && ReferenceEquals(draft, editor.Draft) && editor.Revision == revision && draft.From == from && draft.To == to && draft.Amount == amount && server.Writes.Count == 1,
                $"振替{operation}の人物再取得401は最初の復帰情報と元要求を再利用し、入力・由来・旧版を変更しない");
            await CompleteTransferLogin(fixture, root);
            Check(ReferenceEquals(editor, TransferEditing) && ReferenceEquals(draft, editor.Draft) && controls.All(c => ReferenceEquals(c, Find<Control>(root, c.Name))) &&
                draft.From == from && draft.To == to && draft.Amount == amount && editor.Revision == revision, "同じ本人への復帰は元の入力コントロールとモデルを再利用し値・由来・旧版を変更しない");
            Check(peopleReads == beforePeople + 2 && masters.Reads == beforePayments + 1 && server.Reads.Count == reads && server.Writes.Count == 1,
                "復帰時は人物・支払方法だけを再取得し、振替詳細GET・自動保存・自動再送しない");
            Check(ReferenceEquals(pending, TransferRuntime("transferPending")) && Find<Button>(root, "TransferWriteRetry").Visibility == Visibility.Visible && Find<TextBox>(root, "TransferAmount").IsReadOnly,
                "未完了要求のrequestIdと本文を固定し、復帰後も別保存でなく明示再試行だけを許可する");
            await InvokeAsync(() => Find<Button>(root, "TransferWriteRetry")); await Wait(() => TransferEditing is null);
            Check(server.Writes.Count == 2 && server.Writes[0].Body == server.Writes[1].Body && server.Writes[0].Method == server.Writes[1].Method &&
                server.Rows.Count == (operation == "delete" ? 0 : operation == "reuse" ? 2 : 1), "再認証後の明示再試行は同一要求を送り、登録・更新・流用・削除の正常復帰を維持する");
        }
        await CheckTransferRawRecovery();
        await CheckTransferRawRecovery(retryMasters: false);
        await CheckTransferRecoveryIdentity();
        await CheckTransferRecoveryPermissions();
        await CheckTransferUnknownRecovery();
        await CheckTransferRecoveryCancellation();
    }

    private async Task CheckTransferRawRecovery(bool retryMasters = true)
    {
        using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true };
        var masters = new PaymentServer(); var server = new TransferInputServer(masters); var failMasters = false; var expirePayments = false;
        fixture.PeopleHandler = (_, _) => Task.FromResult(PaymentServer.Json(masters.People()));
        fixture.PaymentsHandler = (request, ct) =>
        {
            if (expirePayments) { expirePayments = false; fixture.RejectAuthentication = true; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)); }
            return failMasters ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)) : masters.HandleAsync(request, ct);
        };
        fixture.TransfersHandler = server.HandleAsync;
        var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync();
        var originalWindow = window ?? throw new InvalidOperationException("振替Windowがありません。"); await originalWindow.OpenTransferEditorAsync(null);
        await TypeTransferName(root, "ATM"); await FocusTransferControl(Find<TextBox>(root, "TransferMemo"));
        await Wait(() => TransferEditing?.Draft.From.Source == TransferSelectionSource.Automatic);
        var editor = TransferEditing ?? throw new InvalidOperationException("編集状態がありません。");
        var from = editor.Draft.From; var to = editor.Draft.To;
        server.SuggestionGate = new(TaskCreationOptions.RunContinuationsAsynchronously); server.RejectSuggestions = true; fixture.RejectAuthentication = true;
        var names = Find<AutoSuggestBox>(root, "TransferName"); var beforeSearch = server.Suggestions;
        await FocusTransferControl(All(names).OfType<TextBox>().First(t => t.IsLoaded)); await PressReceiptKey(0x41, 0x11); await TypeTransferName(root, "再認証の入力");
        await Wait(() => server.Suggestions > beforeSearch);
        var input = All(names).OfType<TextBox>().First(t => t.IsLoaded); input.Select(1, 2);
        Find<DateInputBox>(root, "TransferDate").Text = "2026/"; Find<TimeInputBox>(root, "TransferTime").Text = "時刻未確定"; Find<TextBox>(root, "TransferAmount").Text = "1x";
        await Wait(() => editor.Draft.UnresolvedDateText == "2026/" && editor.Draft.UnresolvedTimeText == "時刻未確定" && editor.Draft.UnresolvedAmountText == "1x");
        originalWindow.AppWindow.Resize(new SizeInt32(700, 560)); var scroller = Find<ScrollViewer>(root, "TransferInputScroller");
        await Wait(() => scroller.ScrollableHeight > 20); scroller.UpdateLayout();
        // 末尾追従による高さ変更と区別できる、確定した途中位置で退避する。
        scroller.ChangeView(null, 20, null, true); await Wait(() => Math.Abs(scroller.VerticalOffset - 20) < 1);
        var offset = scroller.VerticalOffset; server.SuggestionGate.SetResult(true);
        await Wait(() => Visible(root, "LoginPage"));
        var capturedView = TransferRuntime("transferAuthentication") ?? throw new InvalidOperationException("復帰情報がありません。");
        Check(ReferenceEquals(editor, TransferEditing) && editor.Draft.UnresolvedDateText == "2026/" && editor.Draft.UnresolvedTimeText == "時刻未確定" && editor.Draft.UnresolvedAmountText == "1x" && editor.Draft.From == from && editor.Draft.To == to,
            "候補取得の401でも未確定日時・金額・自動補完由来を元モデルに保持する");
        expirePayments = true; server.RejectSuggestions = false;
        await CompleteTransferLogin(fixture, root, mastersExpireAgain: true);
        Check(ReferenceEquals(capturedView, TransferRuntime("transferAuthentication")) && ReferenceEquals(editor, TransferEditing) && ReferenceEquals(window, originalWindow) &&
            ReferenceEquals(names, Find<AutoSuggestBox>(root, "TransferName")) && editor.Draft.UnresolvedDateText == "2026/" && editor.Draft.UnresolvedTimeText == "時刻未確定" &&
            editor.Draft.UnresolvedAmountText == "1x" && editor.Draft.From == from && editor.Draft.To == to && editor.Revision is null && server.Details == 0 && server.Writes.Count == 0,
            "支払方法再取得の連続401でも最初の位置情報と元のWindow・コントロール・未確定入力・選択由来を保持する");
        failMasters = retryMasters;
        var method = masters.Rows.FindIndex(p => p.PaymentMethodId == masters.ActiveId); masters.Rows[method] = masters.Rows[method] with { Name = "最新名" };
        await CompleteTransferLogin(fixture, root);
        if (retryMasters)
        {
            Check(Find<InfoBar>(root, "TransferEditorNotice").Message == "マスタ情報を取得できませんでした" && Find<Button>(root, "TransferMasterRetry").Visibility == Visibility.Visible &&
                ReferenceEquals(editor, TransferEditing) && Find<DateInputBox>(root, "TransferDate").Text == "2026/" && Find<TextBox>(root, "TransferAmount").IsReadOnly && Find<TextBox>(root, "TransferAmount").IsEnabled,
                "マスタ取得失敗は元の入力をコピー可能な読み取り専用で表示し、空マスタで編集再開しない");
            failMasters = false;
            await InvokeAsync(() => Find<Button>(root, "TransferMasterRetry")); await Wait(() => !Find<TextBox>(root, "TransferAmount").IsReadOnly);
        }
        Check(!editor.IsReadOnly && !Find<TextBox>(root, "TransferAmount").IsReadOnly && TransferRuntime("transferAuthentication") is null,
            "連続401後はマスタ取得と現在権限の確認が完了してから編集を再開する");
        try { await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), input) && input.SelectionStart == 1 && input.SelectionLength == 2 && Math.Abs(scroller.VerticalOffset - offset) < 1); }
        catch (TaskCanceledException error)
        {
            var viewType = capturedView.GetType();
            throw new InvalidOperationException($"復帰位置: focusMatches={ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), input)}; selection={input.SelectionStart},{input.SelectionLength}; offset={scroller.VerticalOffset}/{offset}; capturedFocusMatches={ReferenceEquals(viewType.GetProperty("Focus")!.GetValue(capturedView), input)}; capturedSelection={viewType.GetProperty("SelectionStart")!.GetValue(capturedView)},{viewType.GetProperty("SelectionLength")!.GetValue(capturedView)}; capturedOffset={viewType.GetProperty("ScrollOffset")!.GetValue(capturedView)}; inputLoaded={input.IsLoaded}", error);
        }
        Check(ReferenceEquals(editor, TransferEditing) && ReferenceEquals(names, Find<AutoSuggestBox>(root, "TransferName")) && editor.Draft.From == from && editor.Draft.To == to &&
            Find<DateInputBox>(root, "TransferDate").Text == "2026/" && Find<TimeInputBox>(root, "TransferTime").Text == "時刻未確定" && Find<TextBox>(root, "TransferAmount").Text == "1x" &&
            ((ReceiptFilterChoice)Find<ComboBox>(root, "TransferFrom").SelectedItem).Name.Contains("最新名") && editor.HasUnsavedChanges,
            "マスタ再試行は名称・候補だけ更新し、未確定入力・選択由来・文字選択・スクロールと元コントロールを維持する");
        Check(server.Details == 0 && server.Writes.Count == 0 && !Find<Button>(root, "TransferSave").IsEnabled, "未確定入力の復帰で詳細GET・自動補正・保存を行わない");
        await InvokeAsync(() => Find<Button>(root, "LedgerSelector")); await WaitTransferDialog(root, "未保存の内容があります");
        Check(Find<ContentDialog>(root, "TransferConfirmationDialog").SecondaryButtonText == "破棄して移動", "復帰後も通常の家計簿切替の未保存3択保護を維持する");
        await ConfirmTransfer(root, ContentDialogResult.None);
        await InvokeAsync(() => Find<Button>(root, "SettingsButton")); await WaitTransferDialog(root, "未保存の内容があります"); await ConfirmTransfer(root, ContentDialogResult.None);
        Check(TransferEditing == editor && Find<DateInputBox>(root, "TransferDate").Text == "2026/", "通常ナビゲーション取消でも未確定入力を補正・破棄しない");
        Find<NavigationView>(root, "ManagementNavigation").SelectedItem = Find<NavigationViewItem>(root, "LogoutNavigation");
        await WaitTransferDialog(root, "未保存の内容があります"); await ConfirmTransfer(root, ContentDialogResult.None);
        Check(ReferenceEquals(editor, TransferEditing) && fixture.Tokens.Current is not null && Visible(root, "HomePage"), "復帰後の通常ログアウトも未保存確認の取消で入力・認証を保持する");
        await InvokeAsync(() => Find<Button>(root, "TransferBack")); await ConfirmTransfer(root, ContentDialogResult.Secondary); await Wait(() => TransferEditing is null);
    }

    private async Task CheckTransferRecoveryIdentity()
    {
        foreach (var otherEndpoint in new[] { false, true })
        {
            using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true };
            var masters = new PaymentServer(); var server = new TransferInputServer(masters); var saved = server.Add();
            fixture.PeopleHandler = (_, _) => Task.FromResult(PaymentServer.Json(masters.People())); fixture.PaymentsHandler = masters.HandleAsync; fixture.TransfersHandler = server.HandleAsync;
            var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync();
            var originalWindow = window ?? throw new InvalidOperationException("振替Windowがありません。"); await originalWindow.OpenTransferEditorAsync(saved.TransferId);
            await FocusTransferControl(Find<TextBox>(root, "TransferAmount")); Find<TextBox>(root, "TransferAmount").Text = "2000"; await Wait(() => TransferEditing?.Draft.Amount == 2000);
            await SubmitTransferToExpiredLogin(fixture, server, root, "update");
            if (otherEndpoint)
            {
                var change = All(root).OfType<HyperlinkButton>().Single(c => Equals(c.GetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.AutomationIdProperty), "LoginChangeConnection"));
                await FocusTransferControl(change); await PressReceiptKey(0x0D); await Wait(() => Visible(root, "ConnectionPage"));
                fixture.RejectAuthentication = false; Find<TextBox>(root, "ServerUrl").Text = "https://other.example.test/";
                await InvokeAsync(() => Find<Button>(root, "ConnectButton")); await Wait(() => Visible(root, "LoginPage") && Equals(TransferRuntime("busy"), false));
                Check(TransferEditing is null && TransferRuntime("transferPending") is null && fixture.OtherServerRequests.SequenceEqual(["/.well-known/openid-configuration"]),
                    "接続先変更は接続確認後に元の編集を破棄し、元トークンや未完了要求を別サーバーへ送らない");
            }
            else fixture.AccountId = Guid.NewGuid();
            server.WriteError = null; await CompleteTransferLogin(fixture, root);
            Check(ReferenceEquals(window, originalWindow) && TransferEditing is null && TransferRuntime("transferPending") is null && Find<TextBox>(root, "TransferAmount").Text == "" && Find<Grid>(root, "RecordContent").Visibility == Visibility.Visible && server.Writes.Count == 1,
                "別アカウント・別接続先では元の入力と要求を破棄し、通常のログイン後画面へ進む");
        }
    }

    private async Task CheckTransferRecoveryPermissions()
    {
        foreach (var role in new[] { "Viewer", "Editor", "Unmapped", "Removed" })
        {
            using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true };
            var masters = new PaymentServer(); var bank = Guid.NewGuid(); masters.Rows.Add(new(bank, masters.Mine, "本人の銀行", 1, null, null, Guid.NewGuid()));
            var server = new TransferInputServer(masters); var saved = server.Add();
            fixture.PeopleHandler = (_, _) => Task.FromResult(PaymentServer.Json(masters.People())); fixture.PaymentsHandler = masters.HandleAsync; fixture.TransfersHandler = server.HandleAsync;
            var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync();
            var originalWindow = window ?? throw new InvalidOperationException("振替Windowがありません。"); await originalWindow.OpenTransferEditorAsync(saved.TransferId);
            await FocusTransferControl(Find<TextBox>(root, "TransferAmount")); Find<TextBox>(root, "TransferAmount").Text = "2000"; await Wait(() => TransferEditing?.Draft.Amount == 2000);
            if (role == "Editor")
            {
                var to = Find<ComboBox>(root, "TransferTo"); to.SelectedItem = to.Items.Cast<ReceiptFilterChoice>().Single(c => c.Id == bank);
            }
            var editor = TransferEditing ?? throw new InvalidOperationException("編集状態がありません。");
            await SubmitTransferToExpiredLogin(fixture, server, root, "update");
            if (role == "Removed") fixture.CannotOpenLedger = true;
            else { masters.Role = role == "Unmapped" ? "Editor" : role; masters.Mapped = role != "Unmapped"; }
            server.WriteError = null; await CompleteTransferLogin(fixture, root);
            Check(ReferenceEquals(editor, TransferEditing) && editor.IsReadOnly && editor.Revision == saved.Revision && editor.Draft.Amount == 2000 &&
                Find<Button>(root, "TransferSave").Visibility == Visibility.Collapsed && Find<Button>(root, "TransferWriteRetry").Visibility == Visibility.Collapsed && Find<Button>(root, "TransferMasterRetry").Visibility == Visibility.Collapsed && server.Writes.Count == 1,
                $"再認証後の{role}では現在の参加・権限を検証して入力/旧版を保持し、保存・再送を禁止する");
            Check(Find<NavigationViewItem>(root, "LogoutNavigation").IsEnabled && Find<NavigationViewItem>(root, "AccountNavigation").IsEnabled,
                "参加喪失・マスタ失敗でも再ログイン済みのアカウント操作を復元する");
            if (role == "Editor") Check(editor.Draft.To.PaymentMethodId == bank && editor.SavedToPaymentMethodId == saved.ToPaymentMethodId && server.Details == 1,
                "現在の両選択が本人所有でも保存済みの相手側所有権を確認し、追加詳細GETなしで編集を禁止する");
            await InvokeAsync(() => Find<Button>(root, "TransferBack")); await ConfirmTransfer(root, ContentDialogResult.Primary); await Wait(() => TransferEditing is null);
        }
    }

    private async Task CheckTransferUnknownRecovery()
    {
        using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true };
        var masters = new PaymentServer(); var server = new TransferInputServer(masters) { LoseResponse = true };
        fixture.PeopleHandler = (_, _) => Task.FromResult(PaymentServer.Json(masters.People())); fixture.PaymentsHandler = masters.HandleAsync; fixture.TransfersHandler = server.HandleAsync;
        var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync();
        var originalWindow = window ?? throw new InvalidOperationException("振替Windowがありません。"); await originalWindow.OpenTransferEditorAsync(null); await FillTransfer(root, masters);
        await InvokeAsync(() => Find<Button>(root, "TransferSave")); await Wait(() => Find<Button>(root, "TransferWriteRetry").Visibility == Visibility.Visible);
        var pending = TransferRuntime("transferPending"); fixture.RejectAuthentication = true; server.WriteError = HttpStatusCode.Unauthorized;
        await InvokeAsync(() => Find<Button>(root, "TransferWriteRetry")); await Wait(() => Visible(root, "LoginPage"));
        server.WriteError = null; await CompleteTransferLogin(fixture, root);
        Check(Equals(TransferRuntime("transferUnknown"), true) && ReferenceEquals(pending, TransferRuntime("transferPending")) && server.Rows.Count == 1 && server.Writes.Count == 2 && server.Details == 0,
            "結果不明の再送中に401となっても結果不明と同じ元要求を保持し、再認証だけでは再送・詳細GETしない");
        await InvokeAsync(() => Find<Button>(root, "TransferWriteRetry")); await Wait(() => TransferEditing is null);
        Check(server.Rows.Count == 1 && server.Writes.Count == 3 && server.Writes.All(w => w.Body == server.Writes[0].Body), "結果不明の再認証後も同じ要求だけを明示再送し二重登録しない");
    }

    private async Task CheckTransferRecoveryCancellation()
    {
        using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true };
        var masters = new PaymentServer(); var server = new TransferInputServer(masters) { SuggestionGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        fixture.PeopleHandler = (_, _) => Task.FromResult(PaymentServer.Json(masters.People())); fixture.PaymentsHandler = masters.HandleAsync; fixture.TransfersHandler = server.HandleAsync;
        var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync();
        var originalWindow = window ?? throw new InvalidOperationException("振替Windowがありません。"); await originalWindow.OpenTransferEditorAsync(null);
        await TypeTransferName(root, "保持する入力"); await Wait(() => server.Suggestions > 0);
        var editor = TransferEditing;
        // 他の認証操作からの失効通知を模倣し、要求待機中の専用退避経路だけを検証する。
        typeof(MainWindow).GetMethod("StopTransferAuthentication", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(originalWindow, null);
        await Wait(() => Visible(root, "LoginPage")); server.SuggestionGate.SetResult(true); await Settle();
        Check(ReferenceEquals(editor, TransferEditing) && Find<AutoSuggestBox>(root, "TransferName").ItemsSource is null && TransferEditing?.Draft.Name == "保持する入力",
            "再認証への退避は進行中の候補を中断し、遅延応答で隠れた画面や入力を更新しない");
        await InvokeAsync(() => Find<Button>(root, "LoginButton")); await Wait(() => Visible(root, "BrowserPage"));
        // 最後のWindowを閉じると検証プロセス自体が正常終了する。既存ヘルパーで次のWindowを先に開き、対象だけ閉じる。
        using var nextFixture = new SyntheticConnection();
        await OpenSyntheticWindow(nextFixture);
        object? ClosedState(string field) => typeof(MainWindow).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(originalWindow);
        Check(ClosedState("transferEditor") is null && ClosedState("transferAuthentication") is null && ClosedState("transferPending") is null && Equals(ClosedState("closed"), true),
            "再認証中のアプリ終了ではメモリの編集・復帰情報・要求を破棄する");
    }
}
