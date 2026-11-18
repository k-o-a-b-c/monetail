using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Monetail.WinUI.Views.Transfers;
using Monetail.Windows.Client.Transport;
using Windows.Foundation;
using Windows.Graphics;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private static FieldInfo TransferViewField(string name) => typeof(TransferListView).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("一覧の観測状態がありません: " + name);

    private async Task CheckTransferViewportTriggers()
    {
        using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true };
        var masters = new PaymentServer();
        var reads = new List<int>();
        TaskCompletionSource<bool>? pageGate = null;
        fixture.PeopleHandler = (_, _) => Task.FromResult(PaymentServer.Json(masters.People()));
        fixture.PaymentsHandler = masters.HandleAsync;
        fixture.TransfersHandler = async (request, token) =>
        {
            var query = TransferQueryValues(request.RequestUri!);
            var offset = query.TryGetValue("cursor", out var cursor) ? int.Parse(cursor) : 0;
            if (query.TryGetValue("focusTransferId", out var focus))
                offset = int.Parse(Guid.Parse(focus).ToString("N"), System.Globalization.NumberStyles.HexNumber) - 1;
            reads.Add(offset);
            if (pageGate is { } gate)
                await gate.Task.WaitAsync(token);
            return PaymentServer.Json(new TransferListInfo(Enumerable.Range(offset + 1, 100).Select(i => new TransferSummaryInfo(
                Guid.Parse(i.ToString("x32")), new(2026, 11, 10), "行" + i, masters.ActiveId, masters.OtherActiveId, i, null)).ToArray(), (offset + 100).ToString()));
        };
        var root = await OpenSyntheticWindow(fixture);
        await StartSyntheticAsync();
        var active = window ?? throw new InvalidOperationException("Windowがありません。");
        var area = DisplayArea.GetFromWindowId(active.AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        active.AppWindow.Move(new PointInt32(area.X, area.Y));
        active.AppWindow.ResizeClient(new SizeInt32(1100, 780));
        var view = Find<TransferListView>(root, "TransfersView");
        var model = view.ViewModel ?? throw new InvalidOperationException("一覧VMがありません。");
        var list = Find<ListView>(root, "TransferList");
        await Wait(() => list.IsLoaded && All(list).OfType<ScrollViewer>().Any());
        var scroll = All(list).OfType<ScrollViewer>().First();
        await Settle();
        Check(reads.SequenceEqual(new[] { 0 }) && list.Items.Count == 100, "通常の表示領域は初回100件だけを取得する");

        // 物理画面より大きい合成レイアウトで、100件でも埋まらない表示領域を再現する。
        view.Height = 20000;
        try
        {
            await Wait(() => model.Rows.Count == 300 && !model.IsLoading && model.CanContinue);
        }
        catch (OperationCanceledException error)
        {
            var suspended = model.GetType().GetField("suspended", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(model);
            throw new InvalidOperationException($"補充後の状態: rows={model.Rows.Count}, reads={string.Join(',', reads)}, fill={model.AutomaticFillCount}, loading={model.IsLoading}, filled={model.ViewportFilled}, view={view.ActualHeight}, list={list.ActualHeight}, viewport={scroll.ViewportHeight}, extent={scroll.ExtentHeight}, scrollable={scroll.ScrollableHeight}, sameScroller={ReferenceEquals(TransferViewField("scroller").GetValue(view), scroll)}, queued={TransferViewField("viewportQueued").GetValue(view)}, suspended={suspended}, cursor={model.NextCursor}, finding={model.IsFindingSaved}, failure={model.Failure}", error);
        }
        await Settle();
        Check(reads.SequenceEqual(new[] { 0, 100, 200 }) && model.AutomaticFillCount == 2 && scroll.ScrollableHeight == 0,
            "SizeChangedと行追加でレイアウト確定後に2回補充し、300件で自動取得を停止する");
        await Wait(() => !(bool)TransferViewField("viewportQueued").GetValue(view)!);
        model.SearchText = "未確定の下書き";
        model.CanRegister = false;
        Check(!(bool)TransferViewField("viewportQueued").GetValue(view)!, "SearchText/CanRegisterの変更だけでは表示範囲の測定を予約しない");
        await Wait(() => !Find<Button>(root, "TransferRegisterPlaceholder").IsEnabled);
        model.CanRegister = true;
        await Wait(() => Find<Button>(root, "TransferRegisterPlaceholder").IsEnabled);
        Check(reads.Count == 3, "登録CommandのCanExecute通知だけで標準Buttonの有効状態が往復し、検索や取得を行わない");

        view.RememberPosition();
        TransferViewField("scrollIntent").SetValue(view, true);
        TransferViewField("queuedUserScroll").SetValue(view, true);
        view.RestorePosition();
        await Wait(() => !(bool)TransferViewField("scrollIntent").GetValue(view)! && !(bool)TransferViewField("queuedUserScroll").GetValue(view)!);
        view.ObserveViewport();
        await Settle();
        Check(reads.Count == 3 && model.Rows.Count == 300, "表示位置の復元は操作意図を解除し、300件到達後の自動取得を再開しない");
        Check(Find<Button>(root, "TransferContinue").Visibility == Visibility.Visible &&
            ReferenceEquals(Find<Button>(root, "TransferContinue").Command, model.ContinueCommand), "300件到達後の続行ボタンは既存Commandへ接続される");
        await model.ContinueCommand.ExecuteAsync(null);
        await Wait(() => model.Rows.Count == 400 && !model.IsLoading);
        await Settle();
        Check(reads.SequenceEqual(new[] { 0, 100, 200, 300 }) && model.CanContinue, "明示続行だけで次の100件を追加し、領域が空いてもさらに自動取得しない");

        view.Height = 150;
        list.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Visible);
        await Wait(() => scroll.ScrollableHeight > 0 && model.ViewportFilled && !model.CanContinue);
        Check(model.Rows.Count == 400 && reads.Count == 4, "狭い領域へのリサイズで充足状態を再測定し、取得済み行を保持する");
        async Task NearEnd()
        {
            scroll.ChangeView(null, scroll.ScrollableHeight - 30, null, true);
            await Wait(() => scroll.VerticalOffset >= scroll.ScrollableHeight - 31);
            await Settle();
        }
        async Task FinishPage(int expected)
        {
            await Wait(() => model.IsLoading && reads.Count == expected);
            var gate = pageGate ?? throw new InvalidOperationException("追加ページの待機がありません。");
            pageGate = null;
            gate.SetResult(true);
            await Wait(() => !model.IsLoading && model.Rows.Count == expected * 100);
            await Settle();
            Check(reads.Count == expected && model.Rows.Count == expected * 100, "利用者の末尾方向操作は同一カーソルを一度だけ取得する: " + expected);
        }

        await NearEnd();
        pageGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await PositionTransferPointer(root, scroll, scroll.ActualWidth / 2, scroll.ActualHeight / 2);
        SendTransferMouse(0x0800, unchecked((uint)-120));
        await FinishPage(5);

        await NearEnd();
        pageGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var bar = All(scroll).OfType<ScrollBar>().First(b => b.Orientation == Orientation.Vertical);
        var thumb = All(bar).OfType<Thumb>().First(t => t.IsLoaded && t.ActualHeight > 0);
        var point = await PositionTransferPointer(root, thumb, thumb.ActualWidth / 2, thumb.ActualHeight / 2);
        SendTransferMouse(0x0002);
        try
        {
            await Wait(() => (bool)TransferViewField("scrollIntent").GetValue(view)!);
            if (!SetCursorPos(point.X, point.Y + 20))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            await Wait(() => model.IsLoading && reads.Count == 6);
        }
        finally { SendTransferMouse(0x0004); }
        await FinishPage(6);

        await NearEnd();
        pageGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var manipulation = typeof(TransferListView).GetMethod("ListManipulationStarted", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("タッチ相当の開始処理がありません。");
        manipulation.Invoke(view, [scroll, new object()]);
        scroll.ChangeView(null, scroll.ScrollableHeight, null, true);
        await FinishPage(7);

        list.SelectedIndex = list.Items.Count - 5;
        list.ScrollIntoView(list.SelectedItem);
        await Wait(() => list.ContainerFromItem(list.SelectedItem) is ListViewItem { IsLoaded: true, ActualHeight: > 0 });
        ((ListViewItem)list.ContainerFromItem(list.SelectedItem)).Focus(FocusState.Keyboard);
        pageGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await PressReceiptKey(0x23);
        await FinishPage(8);
        Check(reads.SequenceEqual(Enumerable.Range(0, 8).Select(i => i * 100)), "ホイール・スクロールバー・合成タッチ開始・実Endキーは既存の順序で100件ずつ取得する");
        foreach (var target in new[] { 250, 450 })
        {
            var before = reads.Count;
            var id = Guid.Parse(target.ToString("x32"));
            await model.ShowSavedAsync(new("all"), id);
            await Wait(() => list.SelectedItem is not null && list.ContainerFromItem(list.SelectedItem) is FrameworkElement { IsLoaded: true, ActualHeight: > 0 } item &&
                item.TransformToVisual(scroll).TransformPoint(new Point()).Y >= 0 &&
                item.TransformToVisual(scroll).TransformPoint(new Point()).Y < scroll.ViewportHeight);
            await Settle();
            Check(model.SelectedTransferId == id && reads.Skip(before).SequenceEqual(target == 250 ? new[] { 0, 100, 200 } : new[] { 0, 100, 200, 449 }),
                "保存行の選択通知だけで行を可視範囲へ表示し、余分なページを取得しない: " + target);
        }
        view.Height = double.NaN;
    }
    private async Task<ReceiptScreenPoint> PositionTransferPointer(FrameworkElement root, FrameworkElement target, double x, double y)
    {
        await Wait(() => target.IsLoaded && target.ActualWidth > 0 && target.ActualHeight > 0);
        await ActivateReceiptInputWindowAsync();
        var logical = target.TransformToVisual(root).TransformPoint(new Point(x, y));
        var point = new ReceiptScreenPoint { X = (int)Math.Round(logical.X * root.XamlRoot.RasterizationScale), Y = (int)Math.Round(logical.Y * root.XamlRoot.RasterizationScale) };
        if (!ClientToScreen(WinRT.Interop.WindowNative.GetWindowHandle(window!), ref point) || !SetCursorPos(point.X, point.Y))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return point;
    }
    private static void SendTransferMouse(uint flags, uint data = 0)
    {
        ReceiptNativeInput[] input = [new() { Type = 0, Data = new() { Mouse = new() { Flags = flags, Data = data } } }];
        if (SendInput(1, input, Marshal.SizeOf<ReceiptNativeInput>()) != 1)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    private async Task CheckTransferRegistrationCommand()
    {
        foreach (var role in new[] { "Viewer", "Editor" })
        {
            using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true };
            var masters = new PaymentServer { Role = role, Mapped = false };
            fixture.PeopleHandler = (_, _) => Task.FromResult(PaymentServer.Json(masters.People()));
            fixture.PaymentsHandler = masters.HandleAsync;
            var root = await OpenSyntheticWindow(fixture);
            await StartSyntheticAsync();
            var view = Find<TransferListView>(root, "TransfersView");
            await Wait(() => !Find<Button>(root, "TransferRegisterPlaceholder").IsEnabled);
            Check(view.ViewModel is { } model && !model.RegisterCommand.CanExecute(null), "登録Commandだけで権限不足のボタンを無効にする: " + role);
        }
        using (var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true })
        {
            var masters = new PaymentServer();
            var detailGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var server = new TransferInputServer(masters) { DetailGate = detailGate };
            var saved = server.Add();
            fixture.PeopleHandler = (_, _) => Task.FromResult(PaymentServer.Json(masters.People()));
            fixture.PaymentsHandler = masters.HandleAsync;
            fixture.TransfersHandler = server.HandleAsync;
            var root = await OpenSyntheticWindow(fixture);
            await StartSyntheticAsync();
            var view = Find<TransferListView>(root, "TransfersView");
            var model = view.ViewModel ?? throw new InvalidOperationException("一覧VMがありません。");
            var opening = window!.OpenTransferEditorAsync(saved.TransferId);
            await Wait(() => server.Details == 1 && !Find<Button>(root, "TransferRegisterPlaceholder").IsEnabled);
            Check(!model.RegisterCommand.CanExecute(null), "編集画面を開く途中の登録CommandとButtonを無効にする");
            detailGate.SetResult(true);
            await opening;
            var editor = TransferEditing;
            await model.RegisterCommand.ExecuteAsync(null);
            Check(editor is not null && ReferenceEquals(editor, TransferEditing) && Find<Grid>(root, "RecordContent").Visibility == Visibility.Collapsed && server.Details == 1,
                "編集中は既存の一覧非表示と起動ガードを維持し、追加画面を二重起動しない");
            await InvokeAsync(() => Find<Button>(root, "TransferBack"));
            await Wait(() => TransferEditing is null && Find<Button>(root, "TransferRegisterPlaceholder").IsEnabled);
            Check(model.RegisterCommand.CanExecute(null), "編集取消後は同じCommandから登録ボタンの有効状態を復元する");
        }
    }
}
