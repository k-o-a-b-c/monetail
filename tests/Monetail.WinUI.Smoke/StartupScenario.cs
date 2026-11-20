using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Client;
using Monetail.WinUI.Input;
using Monetail.WinUI.Views.Connection;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private static StartupPage? Startup(FrameworkElement root) =>
        (Find<ContentControl>(root, "ConnectionSetupHost").Content as Frame)?.Content as StartupPage;

    private async Task CheckStartupEditorRecovery()
    {
        foreach (var receipt in new[] { false, true })
        {
            using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true };
            var root = await OpenSyntheticWindow(fixture);
            await StartSyntheticAsync();
            if (receipt)
                await window!.OpenReceiptEditorAsync(null);
            else
                await window!.OpenTransferEditorAsync(null);
            var editor = receipt ? (object?)ReceiptEditing : TransferEditing;
            var date = Find<DateInputBox>(root, receipt ? "ReceiptDate" : "TransferDate");
            date.Text = "2026/";
            await ExpireReceiptLogin(fixture);
            await Wait(() => Visible(root, "LoginPage"));
            fixture.RejectSession = fixture.RejectAuthentication = false;
            // 認証取得後の通信失敗で保存情報が残り、接続エラーの再試行へ進む経路を模擬する。
            fixture.Tokens.Current = new TokenSet("synthetic-access", "synthetic-refresh", DateTimeOffset.UtcNow.AddMinutes(10));
            var reads = fixture.Requests.Count;
            await StartSyntheticAsync(reloadSettings: true);
            Check(Visible(root, "HomePage") && Startup(root) is null && date.Text == "2026/" &&
                ReferenceEquals(editor, receipt ? (object?)ReceiptEditing : TransferEditing) &&
                (receipt ? ReceiptView.ViewModel?.IsReadOnly == false : Equals(TransferRuntime("transferMastersFailed"), false)) &&
                fixture.Requests.Skip(reads).Any(path => path.EndsWith("/payment-methods", StringComparison.Ordinal)),
                (receipt ? "レシート" : "振替") + "の保持中編集へ起動再試行から戻る場合も、Page解放でマスタ取得を中断しない");
            Check(!fixture.Requests.Skip(reads).Any(path => path.Contains("/receipts", StringComparison.Ordinal) ||
                path.Contains("/transfers", StringComparison.Ordinal)),
                "起動再試行による編集復帰でも詳細GET・自動保存を行わない");
        }
    }

    private async Task CheckStartupSelection()
    {
        await CheckStartupEditorRecovery();
        using (var fixture = new SyntheticConnection(saved: true))
        {
            fixture.Tokens.Current = null;
            var root = await OpenSyntheticWindow(fixture, (_, initial) =>
                Check(Visible(initial, "LoginPage") && Startup(initial) is null && fixture.Requests.Count == 0,
                    "設定済み・認証なしはActivate前からログインで、起動Pageも通信も生成しない"));
            await StartSyntheticAsync();
            Check(Visible(root, "LoginPage") && fixture.Requests.Count == 0 && Startup(root) is null,
                "認証なしではStartAsyncもネットワーク要求を開始しない");
        }

        using (var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true })
        {
            fixture.Tokens.Current = new TokenSet("synthetic-access", "synthetic-refresh", DateTimeOffset.UtcNow.AddMinutes(-1));
            var root = await OpenSyntheticWindow(fixture);
            await StartSyntheticAsync();
            Check(Visible(root, "HomePage") && fixture.Requests.Take(3).SequenceEqual(
                    (string[])["/connect/token", "/auth/me", "/ledgers/open-last"]),
                "期限切れでも保存情報ありとして起動し、既存の更新・本人確認・家計簿取得だけを行う");
        }

        using (var fixture = new SyntheticConnection(saved: true))
        {
            var root = await OpenSyntheticWindow(fixture);
            Check(Startup(root) is not null, "保存情報があれば有効性を決めつけず起動Pageで検証する");
            fixture.Tokens.Current = null;
            await StartSyntheticAsync();
            Check(Visible(root, "LoginPage") && fixture.Requests.Count == 0 && Startup(root) is null,
                "初回判定後に別プロセスが認証を削除しても再読込し、通信せずログインへ進む");
        }

        using (var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true })
        {
            fixture.ConnectionGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.IgnoreConnectionCancellation = true;
            var root = await OpenSyntheticWindow(fixture);
            var startup = window!.StartAsync();
            await Wait(() => fixture.Requests.Contains("/auth/me"));
            var openSetup = typeof(MainWindow).GetMethod("OpenConnectionSetup",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?? throw new InvalidOperationException("接続設定への入口がありません。");
            openSetup.Invoke(window, []);
            Check(Startup(root) is null && ConnectionView(root) is not null,
                "起動検証中の離脱は起動Pageを解放して接続先設定Pageへ切り替える");
            fixture.ConnectionGate.SetResult(true);
            await startup;
            Check(Visible(root, "ConnectionSetupHost") && Startup(root) is null &&
                !fixture.Requests.Contains("/ledgers/open-last") && fixture.Tokens.Current is not null,
                "取消に遅れた認証応答でも接続設定画面を横取りせず、家計簿取得へ進まない");
        }

        using (var fixture = new SyntheticConnection(saved: true))
        {
            fixture.Tokens.Current = new TokenSet("synthetic-access", null, DateTimeOffset.UtcNow.AddMinutes(10));
            fixture.RejectAuthentication = true;
            fixture.ConnectionGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.IgnoreConnectionCancellation = true;
            var root = await OpenSyntheticWindow(fixture);
            var startup = window!.StartAsync();
            await Wait(() => fixture.Requests.Contains("/auth/me"));
            var openSetup = typeof(MainWindow).GetMethod("OpenConnectionSetup",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?? throw new InvalidOperationException("接続設定への入口がありません。");
            openSetup.Invoke(window, []);
            fixture.Tokens.LoadFailure = new IOException("合成の保存先障害");
            fixture.ConnectionGate.SetResult(true);
            await startup;
            Check(Visible(root, "ConnectionSetupHost") && Startup(root) is null && fixture.Tokens.Current is not null,
                "離脱後の遅い401処理で保存先障害になっても、接続設定画面を失敗画面へ変更しない");
        }

        using (var fixture = new SyntheticConnection(saved: true))
        {
            fixture.ConnectionGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var root = await OpenSyntheticWindow(fixture);
            var startup = window!.StartAsync();
            var host = Find<ContentControl>(root, "ConnectionSetupHost");
            using var next = new SyntheticConnection();
            await OpenSyntheticWindow(next);
            await startup;
            Check(fixture.ConnectionCancelled && host.Content is null &&
                fixture.Tokens.Current is not null && !fixture.Requests.Contains("/ledgers/open-last"),
                "Window終了は起動検証を中断し、Page参照を解放して保存認証を維持する");
        }
    }
}
