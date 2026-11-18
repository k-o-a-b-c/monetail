using System.Globalization;
using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Monetail.WinUI.Views.Receipts;
using Monetail.Windows.Client.Transport;
using Windows.Foundation;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private async Task CheckReceiptSavedRows()
    {
        using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true };
        var masters = new PaymentServer();
        var reads = new List<int>();
        var missing = Guid.NewGuid();
        fixture.PeopleHandler = (_, _) => Task.FromResult(PaymentServer.Json(masters.People()));
        fixture.PaymentsHandler = masters.HandleAsync;
        fixture.ReceiptsHandler = (request, _) =>
        {
            if (request.Method != HttpMethod.Get)
                throw new InvalidOperationException("保存行探索から書込を実行してはいけません。");
            var query = TransferQueryValues(request.RequestUri!);
            var offset = query.TryGetValue("cursor", out var cursor) ? int.Parse(cursor) : 0;
            if (query.TryGetValue("focusReceiptId", out var focus))
            {
                if (Guid.Parse(focus) == missing)
                {
                    reads.Add(-1);
                    return Task.FromResult(PaymentServer.Json(new
                    {
                        code = "not_found"
                    }, HttpStatusCode.NotFound));
                }
                offset = int.Parse(Guid.Parse(focus).ToString("N"), NumberStyles.HexNumber, CultureInfo.InvariantCulture) - 1;
            }
            reads.Add(offset);
            return Task.FromResult(PaymentServer.Json(new ReceiptListInfo(Enumerable.Range(offset + 1, 100).Select(i => new ReceiptSummaryInfo(
                Guid.Parse(i.ToString("x32")), new(2026, 11, 10), "店舗" + i, null, masters.ActiveId, i, null)).ToArray(), (offset + 100).ToString())));
        };
        var root = await OpenSyntheticWindow(fixture);
        await StartSyntheticAsync();
        var view = Find<ReceiptListView>(root, "ReceiptsView");
        var model = view.ViewModel ?? throw new InvalidOperationException("一覧VMがありません。");
        var list = Find<ListView>(root, "ReceiptList");
        await Wait(() => list.IsLoaded && All(list).OfType<ScrollViewer>().Any());
        var scroll = All(list).OfType<ScrollViewer>().First();
        var first = Find<Button>(root, "ReceiptReturnToStart");
        Check(first.Visibility == Visibility.Collapsed, "通常のレシート一覧では先頭復帰ボタンを表示しない");
        foreach (var target in new[] { 250, 450 })
        {
            var before = reads.Count;
            var id = Guid.Parse(target.ToString("x32"));
            await model.ShowSavedAsync(new("all"), id);
            await Wait(() => list.SelectedItem is not null && list.ContainerFromItem(list.SelectedItem) is FrameworkElement { IsLoaded: true, ActualHeight: > 0 } item &&
                item.TransformToVisual(scroll).TransformPoint(new Point()).Y >= 0 &&
                item.TransformToVisual(scroll).TransformPoint(new Point()).Y < scroll.ViewportHeight);
            await Settle();
            Check(model.SelectedReceiptId == id && ReferenceEquals(list.SelectedItem, model.SelectedItem) &&
                reads.Skip(before).SequenceEqual(target == 250 ? new[] { 0, 100, 200 } : new[] { 0, 100, 200, 449 }),
                "レシート保存行を300件以内なら通常探索、それ以外ならfocus取得で選択/表示し、余分に読まない: " + target);
            Check((first.Visibility == Visibility.Visible) == (target == 450), "レシートの先頭復帰はfocus表示中だけ利用できる: " + target);
        }
        var count = reads.Count;
        await InvokeAsync(() => first);
        await Wait(() => model.HasLoaded && !model.IsLoading && !model.IsFocused && model.Rows[0].ReceiptId == Guid.Parse(1.ToString("x32")));
        Check(reads.Skip(count).SequenceEqual(new[] { 0 }) && model.SelectedItem is null && first.Visibility == Visibility.Collapsed,
            "先頭に戻るは同じ条件の最初の100件を一度だけ取得し、focusと選択を解除する");
        count = reads.Count;
        await model.ShowSavedAsync(new("all", focusReceiptId: missing), missing);
        await Settle();
        Check(reads.Skip(count).SequenceEqual(new[] { -1, 0 }) && model.Failure is null && model.SelectedItem is null && !model.IsFocused,
            "レシートfocusの404は一度だけ通常の先頭へ復旧し、存在理由を断定しない");
    }
}
