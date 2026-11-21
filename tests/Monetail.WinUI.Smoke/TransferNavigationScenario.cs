using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Client.Transfers;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private async Task CheckTransferNavigation()
    {
        await CheckHomeResumeLifetime(true);
        using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true };
        var initial = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var initialResume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var continuation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var continuationResume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = new List<string>(); var initialCancelled = false; var continuationCancelled = false;
        var payment = Guid.NewGuid();
        TransferListInfo Page(bool append) => new(Enumerable.Range(append ? 101 : 1, append ? 1 : 100).Select(i => new TransferSummaryInfo(
            Guid.Parse(i.ToString("x32")), new(2026, 11, 10), "画面復帰" + i, payment, Guid.Parse("30000000-0000-0000-0000-000000000000"), i, null)).ToArray(), append ? null : "next");
        fixture.TransfersHandler = async (request, token) =>
        {
            requests.Add(request.RequestUri!.Query); var call = requests.Count;
            var gate = call switch { 1 => initial, 2 => initialResume, 3 => continuation, 4 => continuationResume, _ => throw new InvalidOperationException("余分な振替取得") };
            try { await gate.Task.WaitAsync(token); }
            catch (OperationCanceledException) { if (call == 1) initialCancelled = true; if (call == 3) continuationCancelled = true; throw; }
            return PaymentServer.Json(Page(call >= 3));
        };
        var root = await OpenSyntheticWindow(fixture); var activeWindow = window!;
        // 初回要求を保留するため、取得完了を待つStartSyntheticAsyncはここでは使わない。
        await activeWindow.StartAsync();
        await Wait(() => requests.Count == 1 && Find<ProgressRing>(root, "TransferInitialProgress").IsActive);
        var list = Find<ListView>(root, "TransferList");
        var initialProgress = Find<ProgressRing>(root, "TransferInitialProgress");
        var initialNotice = Find<StackPanel>(root, "TransferInitialNotice");
        await InvokeAsync(() => Find<Button>(root, "SettingsButton"));
        await Wait(() => initialCancelled && Visible(root, "ManagementPage") && Find<ContentControl>(root, "Operations").IsEnabled);
        Check(!initialProgress.IsActive && initialNotice.Visibility == Visibility.Collapsed,
            "初回読込の画面離脱で要求を中断しリングと読み込み表示を停止");
        await InvokeAsync(() => Find<Button>(root, "ReturnButton"));
        list = Find<ListView>(root, "TransferList");
        await Wait(() => requests.Count == 2 && Find<ProgressRing>(root, "TransferInitialProgress").IsActive);
        Check(requests[0] == requests[1] && list.Items.Count == 0, "初回読込の復帰は同じ確定条件・cursorなしで一度だけ再開");
        initialResume.SetResult(true);
        await Wait(() => list.Items.Count == 100 && !Find<ProgressRing>(root, "TransferInitialProgress").IsActive);
        var first = list.Items[0]; var selected = list.Items[4]; list.SelectedItem = selected;
        list.ScrollIntoView(list.Items[79]); await Settle();
        var scroll = All(list).OfType<ScrollViewer>().First();
        var state = Find<Monetail.WinUI.Views.Transfers.TransferListView>(root, "TransfersView").ViewModel!;
        var pending = state.ObserveViewportAsync(79, true, true);
        await Wait(() => requests.Count == 3 && Find<ProgressRing>(root, "TransferNextProgress").IsActive); await Settle();
        var offset = scroll.VerticalOffset; Check(offset > 0, "追加読込中のスクロール保持を先頭以外の表示位置で検証");
        await InvokeAsync(() => Find<Button>(root, "RecordButton"));
        await Settle();
        Check(Math.Abs(scroll.VerticalOffset - offset) < 0.1 && ReferenceEquals(list.SelectedItem, selected) && requests.Count == 3,
            "同じ記録画面の再表示は現在の選択・表示位置を戻さず、読込要求も増やさない");
        var nextProgress = Find<ProgressRing>(root, "TransferNextProgress");
        var nextNotice = Find<StackPanel>(root, "TransferNextNotice");
        await InvokeAsync(() => Find<Button>(root, "SettingsButton"));
        await Wait(() => continuationCancelled && Visible(root, "ManagementPage") && Find<ContentControl>(root, "Operations").IsEnabled);
        await pending;
        Check(!state.IsLoading && !nextProgress.IsActive && nextNotice.Visibility == Visibility.Collapsed,
            "追加読込の画面離脱で要求・状態・末尾リングを同時に停止");
        Check(state.Rows.Count == 100 && ReferenceEquals(state.Rows[0], first) && ReferenceEquals(state.SelectedItem, selected), "追加読込の中断では取得済み行と選択を保持");
        await InvokeAsync(() => Find<Button>(root, "ReturnButton"));
        list = Find<ListView>(root, "TransferList");
        await Wait(() => requests.Count == 4 && state.IsContinuationLoading && Find<ProgressRing>(root, "TransferNextProgress").IsActive); await Settle();
        scroll = All(list).OfType<ScrollViewer>().First();
        Check(requests[2] == requests[3] && requests[3].Contains("cursor=next", StringComparison.Ordinal), "記録画面への復帰で中断した追加ページの同じ条件・カーソルだけを再開");
        Check(ReferenceEquals(list.SelectedItem, selected) && Math.Abs(scroll.VerticalOffset - offset) < 0.1, "追加読込の再開中も選択とスクロール位置を保持");
        continuationResume.SetResult(true);
        await Wait(() => list.Items.Count == 101 && !state.IsLoading && !Find<ProgressRing>(root, "TransferNextProgress").IsActive); await Settle();
        Check(ReferenceEquals(list.Items[0], first) && ReferenceEquals(list.SelectedItem, selected) && Math.Abs(scroll.VerticalOffset - offset) < 0.1,
            "再開成功は末尾追記だけで取得済み行・選択・表示位置を維持");
        initial.SetResult(true); continuation.SetResult(true); await Settle();
        Check(requests.Count == 4 && list.Items.Count == 101 && list.Items.Cast<Monetail.Windows.Presentation.Transfers.TransferListItem>().Select(r => r.Transfer.TransferId).Distinct().Count() == 101,
            "中断した旧要求の遅延完了で行を重複反映せず再開を繰り返さない");
    }
}
