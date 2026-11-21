using System.Net;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Client.Transfers;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private void ShowHomeTestPage(FrameworkElement root, string name)
    {
        var show = typeof(MainWindow).GetMethod("ShowPage", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("画面遷移がありません。");
        show.Invoke(window, [Find<FrameworkElement>(root, name)]);
    }

    private async Task CheckHomeResumeLifetime(bool transfers)
    {
        using (var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true })
        {
            var gates = Enumerable.Range(0, 3).Select(_ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
            var calls = 0;
            async Task<HttpResponseMessage> Load(HttpRequestMessage request, CancellationToken token)
            {
                var call = ++calls;
                if (call > gates.Length)
                    throw new InvalidOperationException("不要なホーム再取得");
                // 中断不能な応答で、VMの世代とWindowの表示復帰の両方を確認する。
                await gates[call - 1].Task;
                var payment = Guid.NewGuid();
                return transfers
                    ? PaymentServer.Json(new TransferListInfo([new(Guid.NewGuid(), new(2026, 11, 10), "復帰" + call,
                        payment, Guid.NewGuid(), call, null)], null))
                    : PaymentServer.Json(new ReceiptListInfo([new(Guid.NewGuid(), new(2026, 11, 10), "復帰" + call,
                        null, payment, call, null)], null));
            }
            if (transfers)
                fixture.TransfersHandler = Load;
            else
                fixture.ReceiptsHandler = Load;
            var root = await OpenSyntheticWindow(fixture);
            await window!.StartAsync();
            await Wait(() => calls == 1);
            var first = TransferRuntime("homeResume") as CancellationTokenSource
                ?? throw new InvalidOperationException("初回復帰の所有者がありません。");
            ShowHomeTestPage(root, "HomePage");
            ShowHomeTestPage(root, "HomePage");
            Check(ReferenceEquals(first, TransferRuntime("homeResume")) && calls == 1,
                "同じホームへの連続表示は復帰処理と初回要求を重複開始しない");
            ShowHomeTestPage(root, "ManagementPage");
            ShowHomeTestPage(root, "HomePage");
            ShowHomeTestPage(root, "ManagementPage");
            ShowHomeTestPage(root, "HomePage");
            await Wait(() => calls == 2);
            var second = TransferRuntime("homeResume");
            Check(first.IsCancellationRequested && !ReferenceEquals(first, second),
                "表示前に取り消した復帰は通信せず、最新のホーム復帰だけが同じ要求を再開する");
            ShowHomeTestPage(root, "ManagementPage");
            ShowHomeTestPage(root, "HomePage");
            await Wait(() => calls == 3);
            var current = TransferRuntime("homeResume");
            var pending = typeof(MainWindow).GetField(transfers ? "transferRestorePending" : "receiptRestorePending",
                BindingFlags.NonPublic | BindingFlags.Instance) ?? throw new InvalidOperationException("表示位置復元の状態がありません。");
            pending.SetValue(window, true);
            gates[0].SetResult(true);
            gates[1].SetResult(true);
            await Settle();
            var list = Find<ListView>(root, transfers ? "TransferList" : "ReceiptList");
            Check(ReferenceEquals(current, TransferRuntime("homeResume")) && Equals(pending.GetValue(window), true) &&
                list.Items.Count == 0 && calls == 3,
                "古い読込の遅延完了は最新の復帰所有・表示位置復元・行に触れず、再取得も追加しない");
            gates[2].SetResult(true);
            await Wait(() => TransferRuntime("homeResume") is null && list.Items.Count == 1);
            Check(Equals(pending.GetValue(window), false) && calls == 3 &&
                !Find<InfoBar>(root, "ErrorNotice").IsOpen,
                "最新の復帰だけが表示位置を復元し、旧結果や重複行を反映せず完了する");
        }
        await CheckHomeResumeFailure(transfers);
    }

    private async Task CheckHomeResumeFailure(bool transfers)
    {
        foreach (var unexpected in new[] { false, true })
        {
            using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true };
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            async Task<HttpResponseMessage> Load(HttpRequestMessage request, CancellationToken token)
            {
                calls++;
                await gate.Task;
                if (unexpected)
                    throw new InvalidOperationException("合成の予期しない読込失敗（画面へ公開しない）");
                return new(HttpStatusCode.ServiceUnavailable);
            }
            if (transfers)
                fixture.TransfersHandler = Load;
            else
                fixture.ReceiptsHandler = Load;
            var root = await OpenSyntheticWindow(fixture);
            await window!.StartAsync();
            await Wait(() => calls == 1);
            if (transfers)
            {
                // 再開中の失敗も、初回と同じ観測経路を通る。
                ShowHomeTestPage(root, "ManagementPage");
                ShowHomeTestPage(root, "HomePage");
                await Wait(() => calls == 2);
            }
            gate.SetResult(true);
            await Wait(() => TransferRuntime("homeResume") is null);
            var notice = Find<InfoBar>(root, "ErrorNotice");
            Check(unexpected
                    ? notice.IsOpen && notice.Message == "一覧を表示できませんでした。"
                    : !notice.IsOpen && Find<StackPanel>(root, transfers ? "TransferInitialNotice" : "ReceiptInitialNotice").Visibility == Visibility.Visible,
                "通常の取得失敗は一覧のエラー、予期しない初回/復帰失敗は観測して共有通知へ表示し、アプリを終了しない");
        }
        using (var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true })
        {
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = false;
            async Task<HttpResponseMessage> Load(HttpRequestMessage request, CancellationToken token)
            {
                started = true;
                await gate.Task;
                throw new InvalidOperationException("終了後の合成読込失敗");
            }
            if (transfers)
                fixture.TransfersHandler = Load;
            else
                fixture.ReceiptsHandler = Load;
            var root = await OpenSyntheticWindow(fixture);
            await window!.StartAsync();
            await Wait(() => started);
            var scope = TransferRuntime("homeResume") as CancellationTokenSource
                ?? throw new InvalidOperationException("終了時の復帰所有者がありません。");
            // 最終Windowを閉じても、後続の非同期終了確認とシナリオを継続する。
            using var next = new SyntheticConnection();
            var keeper = new MainWindow(next.CreateConnection());
            keeper.Activate();
            await Wait(() => ((FrameworkElement)keeper.Content).IsLoaded);
            window.Close();
            gate.SetResult(true);
            await Settle();
            Check(scope.IsCancellationRequested && TransferRuntime("homeResume") is null &&
                !Find<InfoBar>(root, "ErrorNotice").IsOpen,
                "Window終了で復帰を取り消し、終了後の非同期失敗を観測して旧表示を更新しない");
            window = keeper;
        }
    }
}
