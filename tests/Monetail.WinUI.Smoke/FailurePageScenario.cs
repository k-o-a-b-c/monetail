using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Monetail.WinUI.Views.Connection;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private static FailurePage? FailureScreen(FrameworkElement root) =>
        (Find<ContentControl>(root, "AuthenticationHost").Content as Frame)?.Content as FailurePage;

    private static bool FailureDetached(FailurePage page) =>
        new[] { nameof(FailurePage.RetryRequested), nameof(FailurePage.ConnectionChangeRequested), nameof(FailurePage.ExitRequested) }
            .All(name => (typeof(FailurePage).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("接続失敗Pageの操作通知がありません。")).GetValue(page) is null);

    private async Task CheckFailurePageLifecycle()
    {
        using (var fixture = new SyntheticConnection())
        {
            await OpenSyntheticWindow(fixture, (_, initial) =>
                Check(FailureScreen(initial) is null && initial.FindName("FailurePage") is null && fixture.Requests.Count == 0,
                    "未訪問の接続失敗Pageは生成せず、旧常駐Gridも残さない"));
        }
        using (var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true, FailConnection = true })
        {
            var tokens = fixture.Tokens.Current;
            var root = await OpenSyntheticWindow(fixture, (_, initial) =>
                Check(Startup(initial) is not null && FailureScreen(initial) is null && fixture.Requests.Count == 0,
                    "保存認証ありでも失敗Pageは通信失敗時にだけ生成する"));
            await StartSyntheticAsync();
            var first = FailureScreen(root) ?? throw new InvalidOperationException("接続失敗Pageがありません。");
            await Wait(() => first.IsLoaded);
            var retryButton = Find<Button>(root, "RetryButton");
            var change = Find<Button>(root, "FailureChangeConnection");
            var exit = Find<Button>(root, "FailureExit");
            Check(first.NavigationCacheMode == NavigationCacheMode.Disabled && !FailureDetached(first) &&
                Find<TextBlock>(root, "FailureMessage").Text.Length > 0 &&
                All(first).OfType<TextBlock>().Any(text => text.Text == "Monetail") &&
                Equals(retryButton.Content, "再試行") && Equals(change.Content, "接続先の変更") && Equals(exit.Content, "終了") &&
                AutomationProperties.GetAutomationId(retryButton) == "RetryButton" &&
                AutomationProperties.GetAutomationId(change) == "FailureChangeConnection" &&
                AutomationProperties.GetAutomationId(exit) == "FailureExit",
                "失敗Pageは既存メッセージ・3操作・AutomationIdを維持し、表示中だけイベントを接続する");
            await ActivateReceiptInputWindowAsync();
            await FocusTransferControl(retryButton);
            await PressReceiptKey(0x09);
            await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), change));
            await PressReceiptKey(0x09);
            await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), exit));
            await PressReceiptKey(0x09, 0x10);
            await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), change));
            Check(ReferenceEquals(fixture.Tokens.Current, tokens), "通信失敗とキーボード移動で保存認証を削除しない");
            await InvokeAsync(() => Find<Button>(root, "RetryButton"));
            await Wait(() => FailureScreen(root) is { } next && !ReferenceEquals(first, next) && Equals(TransferRuntime("busy"), false));
            var repeated = FailureScreen(root) ?? throw new InvalidOperationException("再失敗のPageがありません。");
            Check(FailureDetached(first) && repeated.IsEnabled && Find<Button>(root, "RetryButton").IsEnabled &&
                ReferenceEquals(fixture.Tokens.Current, tokens), "再試行失敗後も新Pageで操作でき、離脱Pageの3通知を解除する");
            fixture.FailConnection = false;
            fixture.ConnectionGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var reads = fixture.Requests.Count(path => path == "/auth/me");
            await InvokeAsync(() => Find<Button>(root, "RetryButton"));
            await Wait(() => fixture.Requests.Count(path => path == "/auth/me") == reads + 1 && Startup(root) is not null);
            var host = Find<ContentControl>(root, "AuthenticationHost");
            var frame = host.Content;
            var retryMethod = typeof(MainWindow).GetMethod("RetryConnection", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("再試行入口がありません。");
            retryMethod.Invoke(window, [null, null]);
            Check(ReferenceEquals(host.Content, frame) && fixture.Requests.Count(path => path == "/auth/me") == reads + 1,
                "再試行中の重複操作は設定再読込とPage再生成を含めて開始しない");
            fixture.ConnectionGate.SetResult(true);
            await Wait(() => Visible(root, "HomePage") && Equals(TransferRuntime("busy"), false));
            Check(FailureScreen(root) is null && FailureDetached(repeated) && ReferenceEquals(fixture.Tokens.Current, tokens),
                "再試行成功は正常画面へ戻り、失敗Pageとイベントを解放して保存認証を維持する");
        }
        using (var fixture = new SyntheticConnection(saved: true) { FailConnection = true })
        {
            var root = await OpenSyntheticWindow(fixture);
            await StartSyntheticAsync();
            var first = FailureScreen(root) ?? throw new InvalidOperationException("接続失敗Pageがありません。");
            var tokens = fixture.Tokens.Current;
            await InvokeAsync(() => Find<Button>(root, "FailureChangeConnection"));
            await Wait(() => ConnectionSetupVisible(root));
            Check(FailureDetached(first) && FailureScreen(root) is null && ReferenceEquals(fixture.Tokens.Current, tokens),
                "接続先変更では失敗Pageを解放し、設定保存前に認証情報を変更しない");
            ConnectionView(root).ViewModel.CancelCommand.Execute(null);
            await Wait(() => FailureScreen(root) is not null && Equals(TransferRuntime("busy"), false));
            var returned = FailureScreen(root) ?? throw new InvalidOperationException("再訪Pageがありません。");
            Check(!ReferenceEquals(first, returned) && !FailureDetached(returned), "接続失敗画面の再訪は新Pageだけに3通知を接続する");
            var current = window ?? throw new InvalidOperationException("Windowがありません。");
            var host = Find<ContentControl>(root, "AuthenticationHost");
            // 終了ボタンで検証Windowを閉じても、後続シナリオ用のアプリを維持する。
            using var next = new SyntheticConnection();
            var keeper = new MainWindow(next.CreateConnection());
            keeper.Activate();
            await Wait(() => ((FrameworkElement)keeper.Content).IsLoaded);
            await ActivateReceiptInputWindowAsync();
            await FocusTransferControl(Find<Button>(root, "FailureExit"));
            await PressReceiptKey(0x0D);
            var closed = typeof(MainWindow).GetField("closed", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Window終了状態がありません。");
            await Wait(() => Equals(closed.GetValue(current), true));
            Check(host.Content is null && FailureDetached(returned), "終了ボタンはWindowを閉じ、Frame参照と3通知を解放する");
            window = keeper;
        }
    }
}
