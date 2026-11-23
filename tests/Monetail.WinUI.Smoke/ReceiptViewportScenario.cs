using System.Reflection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Monetail.WinUI.Views.Receipts;
using Monetail.Windows.Client.Transport;
using Windows.Graphics;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private static object Viewport(object view) => view.GetType()
        .GetField("viewport", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(view)
        ?? throw new InvalidOperationException("一覧の共通表示状態がありません。");

    private static FieldInfo ViewportField(object controller, string name) => controller.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("一覧の観測状態がありません: " + name);

    private static object? ViewportValue(object view, string name)
    {
        var controller = Viewport(view);
        return ViewportField(controller, name).GetValue(controller);
    }

    private static void SetViewportValue(object view, string name, object value)
    {
        var controller = Viewport(view);
        ViewportField(controller, name).SetValue(controller, value);
    }

    private async Task CheckReceiptViewportBindings()
    {
        using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true };
        var masters = new PaymentServer();
        var categories = new CategoryServer();
        var reads = new List<int>();
        TaskCompletionSource<bool>? nextPage = null;
        fixture.PeopleHandler = (_, _) => Task.FromResult(PaymentServer.Json(masters.People()));
        fixture.PaymentsHandler = masters.HandleAsync;
        fixture.CategoriesHandler = categories.HandleAsync;
        fixture.ReceiptsHandler = async (request, token) =>
        {
            var query = TransferQueryValues(request.RequestUri!);
            var offset = query.TryGetValue("cursor", out var cursor) ? int.Parse(cursor) : 0;
            reads.Add(offset);
            if (nextPage is { } gate)
                await gate.Task.WaitAsync(token);
            return PaymentServer.Json(new ReceiptListInfo(Enumerable.Range(offset + 1, 100).Select(i => new ReceiptSummaryInfo(
                Guid.Parse(i.ToString("x32")), new(2026, 11, 10), "店舗" + i, null, masters.ActiveId, i, null)).ToArray(), (offset + 100).ToString()));
        };
        var root = await OpenSyntheticWindow(fixture);
        await StartSyntheticAsync();
        var active = window ?? throw new InvalidOperationException("Windowがありません。");
        var area = DisplayArea.GetFromWindowId(active.AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        active.AppWindow.Move(new(area.X, area.Y));
        active.AppWindow.ResizeClient(new SizeInt32(900, 780));
        var view = Find<ReceiptListView>(root, "ReceiptsView");
        var model = view.ViewModel ?? throw new InvalidOperationException("一覧VMがありません。");
        var list = Find<ListView>(root, "ReceiptList");
        await Wait(() => list.IsLoaded && All(list).OfType<ScrollViewer>().Any());
        var scroll = All(list).OfType<ScrollViewer>().First();
        await Settle();
        Check(reads.SequenceEqual(new[] { 0 }) && model.Rows.Count == 100, "通常のレシート一覧は初回100件だけを取得する");
        // 物理画面より大きい合成領域で、100件では埋まらない状態を作る。
        view.Height = 40000;
        await Wait(() => model.Rows.Count == 300 && !model.IsLoading && model.CanContinue);
        Check(reads.SequenceEqual(new[] { 0, 100, 200 }) && model.AutomaticFillCount == 2 && scroll.ScrollableHeight == 0,
            "レシートViewの行/サイズ変更は確定レイアウトで最大300件まで補充する");
        await Wait(() => !(bool)ViewportValue(view, "viewportQueued")!);
        Find<TextBox>(root, "RecordKeyword").Text = "未確定店舗";
        model.CanRegister = false;
        Check(!(bool)ViewportValue(view, "viewportQueued")!, "レシートの店名下書き/登録可否の通知は可視範囲測定を予約しない");
        await Wait(() => !Find<Button>(root, "NewReceiptButton").IsEnabled);
        model.CanRegister = true;
        await Wait(() => Find<Button>(root, "NewReceiptButton").IsEnabled);
        Check(reads.Count == 3, "レシート登録ButtonのCanExecute通知は追加GETせず有効状態を往復する");
        view.RememberPosition();
        SetViewportValue(view, "scrollIntent", true);
        SetViewportValue(view, "queuedUserScroll", true);
        view.RestorePosition();
        await Wait(() => !(bool)ViewportValue(view, "scrollIntent")! && !(bool)ViewportValue(view, "queuedUserScroll")!);
        await Settle();
        Check(reads.Count == 3 && model.Rows.Count == 300, "レシートの表示復元は利用者スクロールとみなさず、300件上限を解除しない");
        var more = Find<Button>(root, "ReceiptContinue");
        Check(more.Visibility == Visibility.Visible && ReferenceEquals(more.Command, model.ContinueCommand), "レシートの明示続行はVM Commandへ接続する");
        await model.ContinueCommand.ExecuteAsync(null);
        await Wait(() => model.Rows.Count == 400 && !model.IsLoading);
        await Settle();
        Check(reads.SequenceEqual(new[] { 0, 100, 200, 300 }) && model.CanContinue, "レシートの明示続行は100件だけ追加し、自動補充を再開しない");
        // 実入力の前に、一覧を画面内の通常レイアウトへ戻す。
        var record = Find<Grid>(root, "RecordContent");
        foreach (var child in record.Children.OfType<FrameworkElement>().Where(child => Grid.GetRow(child) == 2))
            child.Visibility = Visibility.Collapsed;
        record.RowDefinitions[2].Height = new GridLength(0);
        record.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
        view.Height = double.NaN;
        await Wait(() => scroll.ViewportHeight > 150 && model.ViewportFilled && !model.CanContinue);
        var selected = model.Rows[0];
        model.SelectedItem = selected;
        // 仮想化後の位置丸めで最下端へ到達しないよう、ホイールで下へ動ける余地を残す。
        var wheelStart = scroll.ScrollableHeight - scroll.ViewportHeight / 2;
        model.ViewPosition = new(Math.Min(80, scroll.ScrollableWidth), wheelStart);
        view.RestorePosition();
        await Wait(() => scroll.VerticalOffset >= wheelStart - 1 && scroll.VerticalOffset < scroll.ScrollableHeight);
        await Settle();
        nextPage = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // SetCursorPosだけで送らず、一覧へ実PointerMovedが到着したことを観測する。
        var moved = false;
        PointerEventHandler pointerMoved = (_, _) => moved = true;
        list.AddHandler(UIElement.PointerMovedEvent, pointerMoved, true);
        try
        {
            await PositionTransferPointer(root, scroll, scroll.ActualWidth / 2, scroll.ActualHeight / 2);
            SendTransferMouse(0x2001, deltaY: 20);
            await Wait(() => moved);
        }
        catch (OperationCanceledException error)
        {
            var point = scroll.TransformToVisual(root).TransformPoint(new(scroll.ActualWidth / 2, scroll.ActualHeight / 2));
            var hits = VisualTreeHelper.FindElementsInHostCoordinates(point, root).Select(element => (element as FrameworkElement)?.Name ?? element.GetType().Name);
            throw new InvalidOperationException($"レシートのPointerMoved未到達: point={point.X},{point.Y}; root={root.ActualWidth}x{root.ActualHeight}; viewport={scroll.ViewportWidth}x{scroll.ViewportHeight}; enabled={list.IsEnabled}; active={GetForegroundWindow() == WinRT.Interop.WindowNative.GetWindowHandle(window!)}; hits={string.Join(',', hits)}", error);
        }
        finally
        {
            list.RemoveHandler(UIElement.PointerMovedEvent, pointerMoved);
        }
        Check(scroll.VerticalOffset < scroll.ScrollableHeight, "実ホイール試験は末尾付近かつ下へ移動できる位置から開始する");
        var beforeWheel = $"offset={scroll.VerticalOffset}/{scroll.ScrollableHeight}; previous={ViewportValue(view, "previousOffset")}; queued={ViewportValue(view, "viewportQueued")}; user={ViewportValue(view, "queuedUserScroll")}; last={(list.ItemsPanelRoot as ItemsStackPanel)?.LastVisibleIndex}; loading={model.IsLoading}; filled={model.ViewportFilled}";
        SendTransferMouse(0x0800, unchecked((uint)-120));
        try
        {
            await Wait(() => model.IsContinuationLoading && reads.Count == 5);
        }
        catch (OperationCanceledException error)
        {
            var panel = list.ItemsPanelRoot as ItemsStackPanel;
            throw new InvalidOperationException($"レシートのホイール取得: offset={scroll.VerticalOffset}/{scroll.ScrollableHeight}; last={panel?.LastVisibleIndex}; rows={model.Rows.Count}; reads={string.Join(',', reads)}; moved={moved}; intent={ViewportValue(view, "scrollIntent")}; previous={ViewportValue(view, "previousOffset")}; beforeWheel=[{beforeWheel}]", error);
        }
        var gate = nextPage ?? throw new InvalidOperationException("追加ページの待機がありません。");
        nextPage = null;
        gate.SetResult(true);
        await Wait(() => !model.IsLoading && model.Rows.Count == 500);
        await Settle();
        Check(reads.SequenceEqual(new[] { 0, 100, 200, 300, 400 }) && ReferenceEquals(model.SelectedItem, selected) && ReferenceEquals(list.ItemsSource, model.Rows),
            "レシートViewの実ホイールは1カーソルだけ取得し、同じ行/選択/ItemsSourceへ追記する");
        var horizontal = scroll.HorizontalOffset;
        var vertical = scroll.VerticalOffset;
        Check(horizontal > 0 && vertical > 0, "レシート一覧は縦横とも実際に移動した位置から編集へ入り、復元を検証する");
        await InvokeAsync(() => Find<Button>(root, "NewReceiptButton"));
        await Wait(() => ReceiptEditing is not null);
        await InvokeAsync(() => Find<Button>(root, "ReceiptBack"));
        await Wait(() => ReceiptEditing is null && Visible(root, "HomePage"));
        var oldView = view;
        view = Find<Monetail.WinUI.Views.Receipts.ReceiptListView>(root, "ReceiptsView");
        list = Find<ListView>(root, "ReceiptList");
        await Wait(() => list.IsLoaded && All(list).OfType<ScrollViewer>().Any());
        scroll = All(list).OfType<ScrollViewer>().First();
        await Wait(() => Math.Abs(scroll.VerticalOffset - vertical) < 1 && Math.Abs(scroll.HorizontalOffset - horizontal) < 1);
        Check(!ReferenceEquals(oldView, view) && oldView.ViewModel is null, "入力からの復帰は旧ホームViewを解放し、新Pageで一覧位置を復元する");
        Check(ReferenceEquals(view.ViewModel, model) && ReferenceEquals(list.SelectedItem, selected) && model.Rows.Count == 500 && reads.Count == 5,
            "レシート新規編集の取消は同じVM/選択と縦横位置へ戻り、一覧を再取得しない");
    }
}
