using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.WinUI.Authentication;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private AuthenticationFlow? CurrentAuthentication => TransferRuntime("authentication") as AuthenticationFlow;

    private static bool AuthenticationReleased(AuthenticationFlow flow) => flow.View is null && flow.Page is null &&
        new[] { "readCancellation", "browserCancellation", "browserLogin", "retry" }.All(name =>
            (typeof(AuthenticationFlow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("認証フローの寿命状態がありません。")).GetValue(flow) is null);

    private async Task CheckAuthenticationFlowLifetime()
    {
        using (var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true })
        {
            var root = await OpenSyntheticWindow(fixture);
            var first = CurrentAuthentication ?? throw new InvalidOperationException("初期認証フローがありません。");
            var frame = first.View ?? throw new InvalidOperationException("認証Frameがありません。");
            await StartSyntheticAsync();
            Check(CurrentAuthentication is null && AuthenticationReleased(first) && frame.Content is null &&
                Find<ContentControl>(root, "AuthenticationHost").Content is null,
                "保存認証から家計簿へ引き渡すとWindowの認証所有を解除し、Page/Frame・取消元・再試行を残さない");
            var logout = typeof(MainWindow).GetMethod("LogoutAsync", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("既存ログアウトがありません。");
            await (Task)(logout.Invoke(window, []) ?? throw new InvalidOperationException("ログアウト結果がありません。"));
            var second = CurrentAuthentication ?? throw new InvalidOperationException("再生成した認証フローがありません。");
            var page = LoginScreen(root) ?? throw new InvalidOperationException("ログインPageがありません。");
            Check(!ReferenceEquals(first, second) && !LoginDetached(page) && fixture.Tokens.Current is null,
                "通常ログアウト後は新規認証フロー/Pageへ必要な4購読だけを接続する");
            fixture.CompleteLogin = true;
            await InvokeAsync(() => Find<Button>(root, "LoginButton"));
            await Wait(() => Visible(root, "HomePage") && Equals(TransferRuntime("busy"), false));
            if (fixture.CallbackTask is { } callback)
                await callback;
            Check(CurrentAuthentication is null && AuthenticationReleased(second) && LoginDetached(page) &&
                Find<ContentControl>(root, "AuthenticationHost").Content is null && fixture.Tokens.Current is not null,
                "再ログイン後も認証フロー・Pageの購読・ブラウザー待機・取消/再試行状態を解放する");
            await second.LoginAsync();
            Check(CurrentAuthentication is null && fixture.BrowserOpens == 1,
                "終了済み認証フローから古いログイン操作を再実行しない");
        }
        foreach (var confirming in new[] { false, true })
        {
            using var fixture = new SyntheticConnection(saved: true) { CompleteLogin = confirming };
            fixture.Tokens.Current = null;
            if (confirming)
            {
                fixture.ConnectionGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                fixture.IgnoreConnectionCancellation = true;
            }
            var root = await OpenSyntheticWindow(fixture);
            var flow = CurrentAuthentication ?? throw new InvalidOperationException("認証フローがありません。");
            var page = LoginScreen(root) ?? throw new InvalidOperationException("ログインPageがありません。");
            await InvokeAsync(() => Find<Button>(root, "LoginButton"));
            await Wait(() => confirming ? fixture.Requests.Contains("/auth/me") : BrowserLoginWaiting(root));
            var openSetup = typeof(MainWindow).GetMethod("OpenConnectionSetup", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("接続先設定への入口がありません。");
            openSetup.Invoke(window, []);
            if (confirming)
                fixture.ConnectionGate!.SetResult(true);
            await Wait(() => Equals(TransferRuntime("busy"), false));
            Check(ConnectionSetupVisible(root) && LoginDetached(page) &&
                !fixture.Requests.Contains("/ledgers/open-last") && !Find<InfoBar>(root, "ErrorNotice").IsOpen,
                "ブラウザー待機/認証検証から離脱した後の古い結果で新Pageや家計簿を開かない");
            var browser = typeof(AuthenticationFlow).GetField("browserLogin", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("ブラウザー待機状態がありません。");
            var cancellation = typeof(AuthenticationFlow).GetField("browserCancellation", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("ブラウザー取消状態がありません。");
            Check(browser.GetValue(flow) is null && cancellation.GetValue(flow) is null,
                "離脱で旧ブラウザー待機と認証検証を中断し、完了後は取消参照を残さない");
        }
        using (var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true })
        {
            var root = await OpenSyntheticWindow(fixture);
            await StartSyntheticAsync();
            await InvokeAsync(() => Find<Button>(root, "SettingsButton"));
            var navigation = Find<NavigationView>(root, "ManagementNavigation");
            navigation.SelectedItem = Find<NavigationViewItem>(root, "ConnectionNavigation");
            await Wait(() => Find<ContentControl>(root, "ConnectionSettingsContent").Content is not null);
            fixture.ConnectionGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.IgnoreConnectionCancellation = true;
            var reads = fixture.Requests.Count;
            await InvokeAsync(() => Find<Button>(root, "ConnectButton"));
            await Wait(() => fixture.Requests.Skip(reads).Contains("/.well-known/openid-configuration"));
            var metadata = fixture.ConnectionGate;
            fixture.ConnectionGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            metadata.SetResult(true);
            await Wait(() => fixture.Requests.Skip(reads).Contains("/auth/me"));
            var pending = CurrentAuthentication ?? throw new InvalidOperationException("接続確認の認証所有者がありません。");
            typeof(MainWindow).GetMethod("OpenConnectionSetup", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, []);
            fixture.ConnectionGate.SetResult(true);
            await Wait(() => Equals(TransferRuntime("busy"), false));
            Check(ConnectionSetupVisible(root) && !fixture.Requests.Skip(reads).Contains("/ledgers/open-last") &&
                typeof(AuthenticationFlow).GetField("readCancellation", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pending) is null,
                "接続先保存後の遅い認証確認もフローのPage寿命で中断し、新設定画面を業務画面へ差し替えない");
        }
        using (var fixture = new SyntheticConnection(saved: true))
        {
            var root = await OpenSyntheticWindow(fixture);
            await StartSyntheticAsync();
            Check(CreationScreen(root) is not null && CurrentAuthentication is null &&
                Find<ContentControl>(root, "AuthenticationHost").Content is null &&
                Find<ContentControl>(root, "LedgerPageHost").Content is Frame,
                "所属0件の家計簿作成は認証後の専用ホストへ引き渡し、認証フローを保持しない");
        }
    }
}
