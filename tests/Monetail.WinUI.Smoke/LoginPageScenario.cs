using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Automation;
using Monetail.WinUI.Views.Authentication;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private static LoginPage? LoginScreen(FrameworkElement root) =>
        ((root.FindName("AuthenticationHost") as ContentControl)?.Content as Frame)?.Content as LoginPage;

    private static bool BrowserLoginWaiting(FrameworkElement root) => LoginScreen(root) is { } page &&
        VisibleAncestors((FrameworkElement)page.FindName("WaitingContent"));

    private static bool LoginDetached(LoginPage page) =>
        typeof(LoginPage).GetField(nameof(LoginPage.LoginRequested), BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(page) is null &&
        typeof(LoginPage).GetField(nameof(LoginPage.ConnectionChangeRequested), BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(page) is null &&
        typeof(LoginPage).GetField(nameof(LoginPage.BrowserOpenRequested), BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(page) is null &&
        typeof(LoginPage).GetField(nameof(LoginPage.CancelRequested), BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(page) is null;

    private async Task CheckLoginPageLifecycle()
    {
        using (var fixture = new SyntheticConnection())
        {
            await OpenSyntheticWindow(fixture, (_, root) =>
                Check(LoginScreen(root) is null && root.FindName("LoginPage") is null && fixture.Requests.Count == 0,
                    "未設定の初回表示ではログインPageも旧ログインGridも先行生成しない"));
        }
        using (var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true })
        {
            var root = await OpenSyntheticWindow(fixture, (_, initial) =>
                Check(Startup(initial) is not null && LoginScreen(initial) is null && fixture.Requests.Count == 0,
                    "保存認証の検証開始前は起動Pageだけを生成し、未訪問のログインPageは存在しない"));
            await StartSyntheticAsync();
            Check(Visible(root, "HomePage") && Find<ContentControl>(root, "AuthenticationHost").Content is null,
                "有効認証はログインPageを経由せず家計簿へ進み、認証前ホストを解放する");
        }
        using (var fixture = new SyntheticConnection(saved: true))
        {
            fixture.Tokens.Current = null;
            var root = await OpenSyntheticWindow(fixture, (_, initial) =>
                Check(LoginScreen(initial) is { NavigationCacheMode: NavigationCacheMode.Disabled, IsEnabled: true } && fixture.Requests.Count == 0,
                    "認証情報なしではActivate前にキャッシュしない標準ログインPageを直接配置する"));
            await StartSyntheticAsync();
            var first = LoginScreen(root) ?? throw new InvalidOperationException("ログインPageがありません。");
            Check(!LoginDetached(first), "表示中のログインPageだけをWindowの操作通知へ接続する");
            await ActivateReceiptInputWindowAsync();
            var loginButton = Find<Button>(root, "LoginButton");
            await FocusTransferControl(loginButton);
            var changeConnection = Find<HyperlinkButton>(root, "LoginChangeConnection");
            await PressReceiptKey(0x09);
            await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), changeConnection));
            Check(FocusManager.GetFocusedElement(root.XamlRoot) is HyperlinkButton link &&
                AutomationProperties.GetAutomationId(link) == "LoginChangeConnection",
                "ログインからTabで接続先変更へ移動し、認証を開始しない");
            await PressReceiptKey(0x09, 0x10);
            await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), loginButton));
            Check(ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), loginButton) && fixture.BrowserOpens == 0,
                "Shift+Tabはログインへ戻り、標準のキーボード順を維持する");
            var show = typeof(MainWindow).GetMethod("ShowLogin", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("ログイン表示の入口がありません。");
            show.Invoke(window, ["合成の認証エラー"]);
            var page = LoginScreen(root) ?? throw new InvalidOperationException("再訪したPageがありません。");
            var notice = Find<InfoBar>(root, "ErrorNotice");
            Check(ReferenceEquals(first, page) && !LoginDetached(first) &&
                notice.IsOpen && notice.Message == "合成の認証エラー" && VisibleAncestors(notice) &&
                !Visible(root, "HomePage") && !Visible(root, "ManagementPage") && fixture.Requests.Count == 0,
                "ログイン中のエラー再表示は同じPageの共有InfoBarを使い、背景画面を閉じたままにする");
            await InvokeAsync(() => Find<Button>(root, "LoginButton"));
            await Wait(() => BrowserLoginWaiting(root));
            Check(ReferenceEquals(LoginScreen(root), page) && !LoginDetached(page) &&
                !Visible(root, "LoginPage") && Find<Button>(root, "ReopenBrowserButton").IsEnabled &&
                Find<Button>(root, "CancelLoginButton").IsEnabled && fixture.BrowserOpens == 1,
                "同じPageの認証待ちではログインを隠し、busy中でも再オープン/取消を使える");
            var waitingUri = fixture.AuthorizationUri;
            var start = typeof(MainWindow).GetMethod("LoginAsync", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("ログイン操作がありません。");
            await (Task)(start.Invoke(window, []) ?? throw new InvalidOperationException("操作結果がありません。"));
            Check(fixture.BrowserOpens == 1 && fixture.AuthorizationUri == waitingUri && ReferenceEquals(LoginScreen(root), page),
                "認証待ち中のログイン再要求では二重開始せず、同じPageと要求URLを維持する");
            var reopen = Find<Button>(root, "ReopenBrowserButton");
            var cancel = Find<Button>(root, "CancelLoginButton");
            await FocusTransferControl(reopen);
            await PressReceiptKey(0x09);
            await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), cancel));
            await PressReceiptKey(0x09, 0x10);
            await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), reopen));
            Check(!VisibleAncestors(loginButton) && !VisibleAncestors(changeConnection),
                "待機中のTab順は再オープン/取消だけで、認証前の操作は表示しない");
            await PressReceiptKey(0x0D);
            await Wait(() => fixture.BrowserOpens == 2);
            Check(fixture.AuthorizationUri == waitingUri && ReferenceEquals(LoginScreen(root), page),
                "待機中のEnterによる再オープンでも同じ認証要求URLを使う");
            await InvokeAsync(() => Find<Button>(root, "CancelLoginButton"));
            await Wait(() => Visible(root, "LoginPage") && Find<Button>(root, "LoginButton").IsEnabled);
            var returned = LoginScreen(root) ?? throw new InvalidOperationException("キャンセル後のPageがありません。");
            Check(ReferenceEquals(page, returned) && !BrowserLoginWaiting(root) &&
                fixture.Tokens.Current is null && !notice.IsOpen,
                "認証取消後は同じPageのログイン表示へ戻り、前回エラーを残さず再試行できる");
            var host = Find<ContentControl>(root, "AuthenticationHost");
            using var next = new SyntheticConnection();
            await OpenSyntheticWindow(next);
            Check(host.Content is null && LoginDetached(returned),
                "Window終了でログインPageと4つのイベント購読を解放する");
        }
        foreach (var confirming in new[] { false, true })
        {
            using var fixture = new SyntheticConnection(saved: true) { CompleteLogin = confirming };
            if (confirming)
                fixture.ConnectionGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Tokens.Current = null;
            var root = await OpenSyntheticWindow(fixture);
            var current = window ?? throw new InvalidOperationException("Windowがありません。");
            var host = Find<ContentControl>(root, "AuthenticationHost");
            await InvokeAsync(() => Find<Button>(root, "LoginButton"));
            await Wait(() => BrowserLoginWaiting(root) && (!confirming || fixture.Requests.Contains("/auth/me")));
            using var next = new SyntheticConnection();
            await OpenSyntheticWindow(next);
            var busy = typeof(MainWindow).GetField("busy", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("操作状態がありません。");
            await Wait(() => Equals(busy.GetValue(current), false));
            Check(host.Content is null && (confirming
                ? fixture.ConnectionCancelled && fixture.Tokens.Current is not null
                : fixture.Tokens.Current is null),
                "認証待ち/確定中のWindow終了は処理を中断し、Pageを再生成せず取得済み認証を保持する");
        }

        using (var fixture = new SyntheticConnection(saved: true) { CompleteLogin = true, HasPreviousLedger = true })
        {
            fixture.Tokens.Current = null;
            fixture.ConnectionGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var root = await OpenSyntheticWindow(fixture);
            var page = LoginScreen(root) ?? throw new InvalidOperationException("ログインPageがありません。");
            await InvokeAsync(() => Find<Button>(root, "LoginButton"));
            await Wait(() => fixture.Requests.Contains("/auth/me"));
            Check(ReferenceEquals(LoginScreen(root), page) && BrowserLoginWaiting(root) &&
                !Find<Button>(root, "ReopenBrowserButton").IsEnabled && !Find<Button>(root, "CancelLoginButton").IsEnabled &&
                !Visible(root, "HomePage"),
                "認証結果の確定中は同じ待機表示で操作を無効化し、未検証の家計簿を表示しない");
            fixture.ConnectionGate.SetResult(true);
            await Wait(() => Visible(root, "HomePage"));
            if (fixture.CallbackTask is { } callback)
                await callback;
            Check(LoginScreen(root) is null && LoginDetached(page),
                "認証成功は家計簿へ進み、同じPageの4通知と参照を解放する");
        }
        using (var fixture = new SyntheticConnection(saved: true) { CompleteLogin = true, HasPreviousLedger = true, RejectAuthentication = true })
        {
            fixture.Tokens.Current = null;
            var root = await OpenSyntheticWindow(fixture);
            var page = LoginScreen(root) ?? throw new InvalidOperationException("ログインPageがありません。");
            await InvokeAsync(() => Find<Button>(root, "LoginButton"));
            await Wait(() => Visible(root, "LoginPage") && Find<InfoBar>(root, "ErrorNotice").IsOpen &&
                Find<Button>(root, "LoginButton").IsEnabled);
            if (fixture.CallbackTask is { } failed)
                await failed;
            Check(ReferenceEquals(LoginScreen(root), page) && !BrowserLoginWaiting(root) && fixture.Tokens.Current is null,
                "OAuth拒否も同じPageの認証前へ戻し、エラーを表示して再試行を許可する");
            var previous = fixture.AuthorizationUri;
            fixture.RejectAuthentication = false;
            await InvokeAsync(() => Find<Button>(root, "LoginButton"));
            await Wait(() => Visible(root, "HomePage"));
            if (fixture.CallbackTask is { } completed)
                await completed;
            Check(fixture.AuthorizationUri != previous && fixture.BrowserOpens == 2 && LoginDetached(page),
                "失敗後の再試行は新しいOAuth要求とし、成功時だけPageを解放する");
        }
    }
}
