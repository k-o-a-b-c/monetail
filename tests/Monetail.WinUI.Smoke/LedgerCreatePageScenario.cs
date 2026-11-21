using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Monetail.WinUI.Views.Ledgers;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private static LedgerCreatePage? CreationScreen(FrameworkElement root) =>
        ((root.FindName("AuthenticationHost") as ContentControl)?.Content as Frame)?.Content as LedgerCreatePage;

    private static bool CreationDetached(LedgerCreatePage page) =>
        new[] { nameof(LedgerCreatePage.CreateRequested), nameof(LedgerCreatePage.RefreshRequested),
            nameof(LedgerCreatePage.LogoutRequested), nameof(LedgerCreatePage.CancelRequested) }
            .All(name => (typeof(LedgerCreatePage).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("作成Pageの操作通知がありません。")).GetValue(page) is null);

    private async Task OpenLedgerAdditionAsync(FrameworkElement root)
    {
        await InvokeAsync(() => Find<Button>(root, "LedgerSelector"));
        await Wait(() => TransferRuntime("ledgerMenu") is MenuFlyout);
        var menu = (MenuFlyout)(TransferRuntime("ledgerMenu") ?? throw new InvalidOperationException("家計簿メニューがありません。"));
        var add = menu.Items.OfType<MenuFlyoutItem>().Last();
        await ActivateReceiptInputWindowAsync();
        await FocusTransferControl(add);
        await PressReceiptKey(0x0D);
        await Wait(() => CreationScreen(root) is { IsLoaded: true });
    }

    private async Task CheckLedgerCreationPage()
    {
        using (var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true })
        {
            var root = await OpenSyntheticWindow(fixture, (_, initial) =>
                Check(CreationScreen(initial) is null && initial.FindName("LedgerCreatePage") is null && initial.FindName("NewLedgerName") is null,
                    "未訪問の作成Page・旧常駐Gridと内部入力をWindowに生成しない"));
            await StartSyntheticAsync();
            Check(CreationScreen(root) is null, "有効な前回家計簿を開く通常起動でも作成Pageは生成しない");
        }
        using (var fixture = new SyntheticConnection(saved: true))
        {
            var root = await OpenSyntheticWindow(fixture);
            await StartSyntheticAsync();
            var page = CreationScreen(root) ?? throw new InvalidOperationException("作成Pageがありません。");
            var name = Find<TextBox>(root, "NewLedgerName");
            var create = Find<Button>(root, "CreateLedgerButton");
            var refresh = Find<Button>(root, "RefreshLedgersButton");
            var logout = Find<Button>(root, "CreatePageLogoutButton");
            Check(!CreationDetached(page) && VisibleAncestors(Find<TextBlock>(root, "NoLedgersNotice")) &&
                VisibleAncestors(refresh) && VisibleAncestors(logout) && Find<Button>(root, "CancelLedgerCreation").Visibility == Visibility.Collapsed,
                "所属0件の同じPageは招待案内・更新・ログアウトを表示しキャンセルを出さない");
            name.Text = "  保持する家計簿  ";
            await ActivateReceiptInputWindowAsync();
            await FocusTransferControl(name);
            await PressReceiptKey(0x0D);
            await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), create));
            Check(fixture.Creations.Count == 0, "名前のEnterは作成ボタンへ移動するだけで送信しない");
            await PressReceiptKey(0x0D, 0x10);
            await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), name));
            await PressReceiptKey(0x09);
            await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), create));
            await PressReceiptKey(0x09);
            await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), refresh));
            await PressReceiptKey(0x09);
            await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), logout));
            Check(fixture.Requests.Count == 3, "所属0件のTab順は名前・作成・更新・ログアウトで、移動だけでは通信しない");
            fixture.FailCreation = true;
            await InvokeAsync(() => create);
            await Wait(() => fixture.Creations.Count == 1 && Equals(TransferRuntime("busy"), false));
            var draft = TransferRuntime("creationDraft");
            await InvokeAsync(() => refresh);
            await Wait(() => Equals(TransferRuntime("busy"), false));
            Check(ReferenceEquals(CreationScreen(root), page) && ReferenceEquals(Find<TextBox>(root, "NewLedgerName"), name) &&
                name.Text == "  保持する家計簿  " && ReferenceEquals(TransferRuntime("creationDraft"), draft) && !Find<InfoBar>(root, "ErrorNotice").IsOpen,
                "更新後も0件なら同じPage・入力・同名再送要求を保ち、新しい結果通知を出さない");
            var tokens = fixture.Tokens.Current;
            fixture.FailLedgerRead = true;
            await InvokeAsync(() => refresh);
            await Wait(() => Equals(TransferRuntime("busy"), false));
            Check(ReferenceEquals(CreationScreen(root), page) && name.Text == "  保持する家計簿  " &&
                Find<InfoBar>(root, "ErrorNotice").IsOpen && ReferenceEquals(tokens, fixture.Tokens.Current),
                "更新の通信失敗は共有エラーだけを表示し、名前・Page・保存認証を保持する");
            fixture.FailLedgerRead = false;
            fixture.LedgerReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var reads = fixture.Requests.Count;
            await InvokeAsync(() => refresh);
            await Wait(() => fixture.Requests.Count == reads + 1);
            Check(!page.IsEnabled && !refresh.IsEnabled && Equals(TransferRuntime("busy"), true),
                "更新中は既存busyとPage.IsEnabledで入力と操作の二重実行を防ぐ");
            foreach (var operation in new[] { "RefreshLedgerMembership", "LogoutFromCreation" })
                typeof(MainWindow).GetMethod(operation, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [page, null]);
            typeof(MainWindow).GetMethod("CreateLedger", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [page, "別の名前"]);
            Check(fixture.Requests.Count == reads + 1 && fixture.Creations.Count == 1,
                "処理中の更新・作成・ログアウトの通知が重なっても別の要求を開始しない");
            fixture.LedgerReadGate.SetResult(true);
            await Wait(() => Equals(TransferRuntime("busy"), false));
            fixture.LedgerReadGate = null;
            fixture.FailLogout = true;
            await InvokeAsync(() => logout);
            await Wait(() => Equals(TransferRuntime("busy"), false));
            Check(ReferenceEquals(CreationScreen(root), page) && page.IsEnabled && name.Text == "  保持する家計簿  " &&
                fixture.Tokens.Current is not null && Find<InfoBar>(root, "ErrorNotice").IsOpen,
                "ログアウト通信失敗でも同じ作成Page・入力・認証を保持し再操作できる");
            fixture.FailCreation = false;
            var before = fixture.Requests.Count;
            await InvokeAsync(() => create);
            await Wait(() => Visible(root, "HomePage") && Equals(TransferRuntime("busy"), false));
            Check(fixture.Creations.Count == 2 && fixture.Creations[0] == fixture.Creations[1] && CreationDetached(page) && CreationScreen(root) is null &&
                !fixture.Requests.Skip(before).Any(path => path == "/ledgers/open-last" || path.EndsWith("/open", StringComparison.Ordinal)),
                "更新と失敗を挟んだ同名作成再試行もrequestIdを保持し、成功応答から直接ホームへ進みPageを解放する");
        }
        foreach (var (count, previous) in new[] { (1, false), (2, false), (1, true) })
        {
            using var fixture = new SyntheticConnection(saved: true);
            var root = await OpenSyntheticWindow(fixture);
            await StartSyntheticAsync();
            var page = CreationScreen(root) ?? throw new InvalidOperationException("作成Pageがありません。");
            Find<TextBox>(root, "NewLedgerName").Text = "更新前の入力";
            fixture.ChoiceCount = count;
            fixture.HasPreviousLedger = previous;
            var reads = fixture.Requests.Count;
            await InvokeAsync(() => Find<Button>(root, "RefreshLedgersButton"));
            await Wait(() => Visible(root, previous ? "HomePage" : "LedgerChoicePage") && Equals(TransferRuntime("busy"), false));
            Check(CreationScreen(root) is null && CreationDetached(page) &&
                !fixture.Requests.Skip(reads).Any(path => path.EndsWith("/open", StringComparison.Ordinal)) &&
                fixture.Requests.Skip(reads).Count(path => path == "/ledgers") == (previous ? 0 : 1),
                $"更新後の所属{count}件・有効な最終家計簿{previous}は既存Workspaceで遷移し、不要な開く要求を追加しない");
        }
        using (var fixture = new SyntheticConnection(saved: true))
        {
            var root = await OpenSyntheticWindow(fixture);
            await StartSyntheticAsync();
            var page = CreationScreen(root) ?? throw new InvalidOperationException("作成Pageがありません。");
            await InvokeAsync(() => Find<Button>(root, "CreatePageLogoutButton"));
            await Wait(() => Visible(root, "LoginPage") && Equals(TransferRuntime("busy"), false));
            Check(fixture.Tokens.Current is null && CreationDetached(page) && CreationScreen(root) is null,
                "所属0件のログアウト成功は既存の認証情報削除とログイン遷移を使い、作成Pageを解放する");
        }
        using (var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true, ChoiceCount = 1 })
        {
            var root = await OpenSyntheticWindow(fixture);
            await StartSyntheticAsync();
            var ledger = TransferRuntime("currentLedger");
            await OpenLedgerAdditionAsync(root);
            var first = CreationScreen(root) ?? throw new InvalidOperationException("追加Pageがありません。");
            Check(Find<TextBlock>(root, "NoLedgersNotice").Visibility == Visibility.Collapsed &&
                Find<Button>(root, "RefreshLedgersButton").Visibility == Visibility.Collapsed &&
                Find<Button>(root, "CreatePageLogoutButton").Visibility == Visibility.Collapsed && VisibleAncestors(Find<Button>(root, "CancelLedgerCreation")),
                "後からの追加は同じPage型を使い、案内・更新・ログアウトを隠してキャンセルを表示する");
            Find<TextBox>(root, "NewLedgerName").Text = "追加を取消";
            await InvokeAsync(() => Find<Button>(root, "CancelLedgerCreation"));
            await Wait(() => Visible(root, "HomePage"));
            Check(CreationDetached(first) && CreationScreen(root) is null && ReferenceEquals(TransferRuntime("currentLedger"), ledger),
                "追加キャンセルは元の家計簿へ戻り、4通知とPage参照を解放する");
            await OpenLedgerAdditionAsync(root);
            var second = CreationScreen(root) ?? throw new InvalidOperationException("再訪Pageがありません。");
            var name = Find<TextBox>(root, "NewLedgerName");
            Check(!ReferenceEquals(first, second) && name.Text.Length == 0 && !CreationDetached(second),
                "作成Pageの通常再訪は新しい入力と4通知で開始する");
            name.Text = "追加の家計簿";
            await ActivateReceiptInputWindowAsync();
            await FocusTransferControl(name);
            await PressReceiptKey(0x0D, 0x11);
            await Wait(() => Visible(root, "HomePage") && Equals(TransferRuntime("busy"), false));
            Check(fixture.Creations.Count == 1 && CreationDetached(second), "追加Pageでも既存Ctrl+Enterは作成を1回だけ通知する");
        }
        foreach (var closing in new[] { false, true })
        {
            using var fixture = new SyntheticConnection(saved: true) { IgnoreConnectionCancellation = true };
            var root = await OpenSyntheticWindow(fixture);
            await StartSyntheticAsync();
            var current = window ?? throw new InvalidOperationException("Windowがありません。");
            var page = CreationScreen(root) ?? throw new InvalidOperationException("作成Pageがありません。");
            var host = Find<ContentControl>(root, "AuthenticationHost");
            fixture.LedgerReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var reads = fixture.Requests.Count;
            await InvokeAsync(() => Find<Button>(root, "RefreshLedgersButton"));
            await Wait(() => fixture.Requests.Count == reads + 1);
            using var next = new SyntheticConnection();
            if (closing)
                await OpenSyntheticWindow(next);
            else
                typeof(MainWindow).GetMethod("OpenConnectionSetup", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, []);
            fixture.LedgerReadGate.SetResult(true);
            var busyField = typeof(MainWindow).GetField("busy", BindingFlags.NonPublic | BindingFlags.Instance)!;
            await Wait(() => Equals(busyField.GetValue(current), false));
            Check(CreationDetached(page) && (closing ? host.Content is null : ConnectionSetupVisible(root) && !Find<InfoBar>(root, "ErrorNotice").IsOpen),
                "更新中のPage離脱/Window終了は購読とFrameを解放し、遅い応答で新画面を上書きしない");
        }
    }
}
