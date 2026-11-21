using System.Net;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Presentation.Receipts;
using Monetail.Windows.Presentation.Transfers;
using Monetail.WinUI.Views.Receipts;
using Monetail.WinUI.Views.Transfers;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private void ShowHomeTestPage(FrameworkElement root, string name)
    {
        var show = typeof(MainWindow).GetMethod("NavigateWorkspace", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("画面遷移がありません。");
        show.Invoke(window, [Enum.Parse(show.GetParameters()[0].ParameterType, name)]);
    }

    private async Task CheckHomeResumeLifetime(bool transfers)
    {
        using (var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true })
        {
            var gates = Enumerable.Range(0, 3).Select(_ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
            var calls = 0;
            var priming = true;
            async Task<int> Load(CancellationToken token)
            {
                if (priming)
                    await Task.Delay(Timeout.Infinite, token);
                var call = ++calls;
                if (call > gates.Length)
                    throw new InvalidOperationException("不要なホーム再取得");
                // 認証通信の直列化とは分離し、中断不能な取得結果をVM境界で再現する。
                await gates[call - 1].Task;
                return call;
            }
            var root = await OpenSyntheticWindow(fixture);
            await StartSyntheticAsync();
            await Wait(() => TransferRuntime("homeResume") is null);
            typeof(MainWindow).GetMethod("ResetRecordHome", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, []);
            var search = new RecordSearchState(new(2026, 11, 10));
            var payment = Guid.NewGuid();
            Task initial;
            var receiptModel = new ReceiptListViewModel((_, _) => Task.FromResult(new ReceiptListInfo([], null)), search, _ => "支払方法");
            var transferModel = new TransferListViewModel((_, _) => Task.FromResult(new TransferListInfo([], null)), search, _ => "支払方法");
            if (transfers)
            {

                var model = new TransferListViewModel(async (_, token) =>
                {
                    var call = await Load(token);
                    return new TransferListInfo([new(Guid.NewGuid(), new(2026, 11, 10), "復帰" + call,
                        payment, Guid.NewGuid(), call, null)], null);
                }, search, _ => "支払方法");
                transferModel = model;
                initial = model.StartAsync(search.TransferQuery());
                model.Suspend();
            }
            else
            {

                var model = new ReceiptListViewModel(async (_, token) =>
                {
                    var call = await Load(token);
                    return new ReceiptListInfo([new(Guid.NewGuid(), new(2026, 11, 10), "復帰" + call,
                        null, payment, call, null)], null);
                }, search, _ => "支払方法");
                receiptModel = model;
                initial = model.StartAsync(search.Receipts.Query());
                model.Suspend();
            }
            await initial;
            priming = false;
            var home = new Monetail.Windows.Presentation.Workspace.RecordHomeViewModel(search, receiptModel, transferModel);
            typeof(MainWindow).GetField("recordHome", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, home);
            ShowHomeTestPage(root, "HomePage");
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
            Monetail.Windows.Presentation.Lists.IRecordListState positionState = transfers ? transferModel : receiptModel;
            var position = new Monetail.Windows.Presentation.Lists.ListViewportPosition(0, 99);
            positionState.ViewPosition = position;
            gates[0].SetResult(true);
            gates[1].SetResult(true);
            await Settle();
            var list = Find<ListView>(root, transfers ? "TransferList" : "ReceiptList");
            Check(ReferenceEquals(current, TransferRuntime("homeResume")) && positionState.ViewPosition == position &&
                list.Items.Count == 0 && calls == 3,
                "古い読込の遅延完了は最新の復帰所有・表示位置復元・行に触れず、再取得も追加しない");
            gates[2].SetResult(true);
            await Wait(() => TransferRuntime("homeResume") is null && list.Items.Count == 1);
            Check(positionState.ViewPosition == position && calls == 3 &&
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
                await gate.Task.WaitAsync(token);
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
                    : !notice.IsOpen && Find<StackPanel>(root, transfers ? "TransferInitialFailure" : "ReceiptInitialFailure").Visibility == Visibility.Visible,
                "通常の取得失敗は一覧のエラー、予期しない初回/復帰失敗は観測して共有通知へ表示し、アプリを終了しない");
        }
        using (var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true })
        {
            Task<HttpResponseMessage> Reject(HttpRequestMessage request, CancellationToken token) =>
                Task.FromException<HttpResponseMessage>(new AuthenticationRejectedException());
            if (transfers)
                fixture.TransfersHandler = Reject;
            else
                fixture.ReceiptsHandler = Reject;
            var root = await OpenSyntheticWindow(fixture);
            var host = Find<Frame>(root, "WorkspaceHost");
            Monetail.WinUI.Views.Workspace.RecordHomePage? rejectedPage = null;
            void CaptureHome(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs args) =>
                rejectedPage = args.Content as Monetail.WinUI.Views.Workspace.RecordHomePage;
            host.Navigated += CaptureHome;
            await window!.StartAsync();
            await Wait(() => LoginScreen(root) is not null && TransferRuntime("homeResume") is null);
            host.Navigated -= CaptureHome;
            Check(host.Content is null && rejectedPage is not null && rejectedPage.ViewModel is null &&
                ((Monetail.WinUI.Views.Receipts.ReceiptListView)rejectedPage.FindName("ReceiptsView")).ViewModel is null &&
                ((Monetail.WinUI.Views.Transfers.TransferListView)rejectedPage.FindName("TransfersView")).ViewModel is null,
                "初回一覧の即時認証失効は既存ログインへ退避し、旧ホームの後続一覧を生成しない");
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
            var closingNotice = Find<InfoBar>(root, "ErrorNotice");
            // 最終Windowを閉じても、後続の非同期終了確認とシナリオを継続する。
            using var next = new SyntheticConnection();
            var keeper = new MainWindow(next.CreateConnection());
            keeper.Activate();
            await Wait(() => ((FrameworkElement)keeper.Content).IsLoaded);
            window.Close();
            gate.SetResult(true);
            await Settle();
            Check(scope.IsCancellationRequested && TransferRuntime("homeResume") is null &&
                !closingNotice.IsOpen,
                "Window終了で復帰を取り消し、終了後の非同期失敗を観測して旧表示を更新しない");
            window = keeper;
        }
    }
}
