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

    private static bool LoginDetached(LoginPage page) =>
        typeof(LoginPage).GetField(nameof(LoginPage.LoginRequested), BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(page) is null &&
        typeof(LoginPage).GetField(nameof(LoginPage.ConnectionChangeRequested), BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(page) is null;

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
            await PressReceiptKey(0x09);
            Check(FocusManager.GetFocusedElement(root.XamlRoot) is HyperlinkButton link &&
                AutomationProperties.GetAutomationId(link) == "LoginChangeConnection",
                "ログインからTabで接続先変更へ移動し、認証を開始しない");
            await PressReceiptKey(0x09, 0x10);
            Check(ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), loginButton) && fixture.BrowserOpens == 0,
                "Shift+Tabはログインへ戻り、標準のキーボード順を維持する");
            var show = typeof(MainWindow).GetMethod("ShowLogin", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("ログイン表示の入口がありません。");
            show.Invoke(window, ["合成の認証エラー"]);
            var page = LoginScreen(root) ?? throw new InvalidOperationException("再訪したPageがありません。");
            var notice = Find<InfoBar>(root, "ErrorNotice");
            Check(!ReferenceEquals(first, page) && LoginDetached(first) &&
                notice.IsOpen && notice.Message == "合成の認証エラー" && VisibleAncestors(notice) &&
                !Visible(root, "HomePage") && !Visible(root, "ManagementPage") && fixture.Requests.Count == 0,
                "通常再訪は新しいPageとし、旧イベントを解放して共有InfoBarを表示し、背景画面は閉じる");
            await InvokeAsync(() => Find<Button>(root, "LoginButton"));
            await Wait(() => Visible(root, "BrowserPage"));
            Check(LoginScreen(root) is null && LoginDetached(page) && !page.IsEnabled && fixture.BrowserOpens == 1,
                "ログインはWindowで一度だけ開始し、離脱したPageのイベントと参照を解放する");
            await InvokeAsync(() => Find<Button>(root, "CancelLoginButton"));
            await Wait(() => Visible(root, "LoginPage") && Find<Button>(root, "LoginButton").IsEnabled);
            var returned = LoginScreen(root) ?? throw new InvalidOperationException("キャンセル後のPageがありません。");
            Check(!ReferenceEquals(page, returned) && fixture.Tokens.Current is null && !notice.IsOpen,
                "認証取消後は新しい操作可能なPageへ戻り、前回エラーを残さない");
            var host = Find<ContentControl>(root, "AuthenticationHost");
            using var next = new SyntheticConnection();
            await OpenSyntheticWindow(next);
            Check(host.Content is null && LoginDetached(returned),
                "Window終了でログインPageと2つのイベント購読を解放する");
        }
        using (var fixture = new SyntheticConnection(saved: true))
        {
            fixture.Tokens.Current = null;
            var root = await OpenSyntheticWindow(fixture);
            var current = window ?? throw new InvalidOperationException("Windowがありません。");
            var host = Find<ContentControl>(root, "AuthenticationHost");
            await InvokeAsync(() => Find<Button>(root, "LoginButton"));
            await Wait(() => Visible(root, "BrowserPage"));
            using var next = new SyntheticConnection();
            await OpenSyntheticWindow(next);
            var busy = typeof(MainWindow).GetField("busy", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("操作状態がありません。");
            await Wait(() => Equals(busy.GetValue(current), false));
            Check(host.Content is null && fixture.Tokens.Current is null,
                "ブラウザー認証待ちのWindow終了は処理を中断し、ログインPageを再生成しない");
        }
    }
}
