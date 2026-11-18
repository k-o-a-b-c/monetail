using System.Globalization;
using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transfers;
using Monetail.Windows.Client.Transport;
using Windows.Foundation;
using Windows.Graphics;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private static Dictionary<string, string> TransferQueryValues(Uri uri) => uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
    private async Task CheckTransferList()
    {
        await CheckTransferMvvmBoundary();
        using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true };
        var masters = new PaymentServer(); var transfers = new List<Uri>(); var receipts = new List<Uri>();
        var count = 0; var fail = false; var invalidCursor = false;
        fixture.PeopleHandler = (_, _) => Task.FromResult(PaymentServer.Json(masters.People())); fixture.PaymentsHandler = masters.HandleAsync;
        fixture.ReceiptsHandler = (request, _) => { receipts.Add(request.RequestUri!); return Task.FromResult(PaymentServer.Json(new ReceiptListInfo([], null))); };
        fixture.TransfersHandler = (request, _) =>
        {
            transfers.Add(request.RequestUri!); var q = TransferQueryValues(request.RequestUri!);
            if (fail) { fail = false; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); }
            if (invalidCursor && q.ContainsKey("cursor")) { invalidCursor = false; return Task.FromResult(PaymentServer.Json(new { code = "invalid_cursor", field = "cursor", message = "internal", latest = (object?)null, permissions = (object?)null }, HttpStatusCode.BadRequest)); }
            var offset = q.ContainsKey("cursor") ? 100 : 0;
            var rows = Enumerable.Range(offset, Math.Min(100, Math.Max(0, count - offset))).Select(i => new TransferSummaryInfo(
                Guid.Parse((i + 1).ToString("x32")), new(2026, 11, 10, 12, 34, 56), "振替" + i, masters.ActiveId, masters.OtherActiveId, 12345, null)).ToArray();
            return Task.FromResult(PaymentServer.Json(new TransferListInfo(rows, count > offset + 100 ? "next" : null)));
        };
        var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync(); await Wait(() => window!.TransferHomeReady);
        var list = Find<ListView>(root, "TransferList"); var filter = Find<ScrollViewer>(root, "RecordFilterScroller");
        var owner = Find<ComboBox>(root, "ReceiptOwner"); var payment = Find<ComboBox>(root, "ReceiptPayment");
        Check(transfers.Count == 1 && Find<TextBlock>(root, "TransferEmpty").Visibility == Visibility.Visible, "振替0件は中央表示し追加要求しない");
        Check(((ReceiptFilterChoice)owner.SelectedItem).Id is null && ((ReceiptFilterChoice)payment.SelectedItem).Id is null, "共通の支払者は全員・支払方法は全件で初期化");
        Check(Find<Button>(root, "TransferRegisterPlaceholder").IsEnabled && !All(root).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "BalanceRegisterPlaceholder").IsEnabled, "振替の登録入口は有効・残高の未実装入口は無効");
        root.UpdateLayout();
        var content = Find<Grid>(root, "RecordContent"); var cards = content.Children.OfType<Border>().ToArray();
        var receiptCard = cards.Single(b => Grid.GetRow(b) == 1); var transferCard = cards.Single(b => Grid.GetRow(b) == 2 && Grid.GetColumn(b) == 0);
        var balanceCard = cards.Single(b => Grid.GetRow(b) == 2 && Grid.GetColumn(b) == 1);
        Check(Grid.GetColumnSpan(receiptCard) == 2 && Math.Abs(receiptCard.ActualHeight / transferCard.ActualHeight - 1.5) < 0.02 && Math.Abs(transferCard.ActualWidth / balanceCard.ActualWidth - 2) < 0.02,
            $"記録ホームは全幅上段60%・下段40%、振替2/3と残高1/3: receipt={receiptCard.ActualWidth}x{receiptCard.ActualHeight}, transfer={transferCard.ActualWidth}x{transferCard.ActualHeight}, balance={balanceCard.ActualWidth}x{balanceCard.ActualHeight}");
        await Settle(); Check(filter.ScrollableWidth < 1 && filter.ComputedHorizontalScrollBarVisibility == Visibility.Collapsed, "通常幅の共通フィルターは不要な横スクロールバーを表示しない");
        window!.AppWindow.ResizeClient(new SizeInt32(760, 620)); await Settle();
        Check(filter.ScrollableWidth > 0 && Find<StackPanel>(root, "RecordFilterRow").Orientation == Orientation.Horizontal && owner.Visibility == Visibility.Visible && payment.Visibility == Visibility.Visible,
            "狭幅でも全項目を1行のまま維持しフィルター内だけ横スクロール");
        filter.ChangeView(0, null, null, true); Find<Button>(root, "RecordMonthApply").Focus(FocusState.Keyboard);
        FocusManager.TryMoveFocus(FocusNavigationDirection.Next, new FindNextElementOptions { SearchRoot = root }); await Settle();
        FocusManager.TryMoveFocus(FocusNavigationDirection.Next, new FindNextElementOptions { SearchRoot = root });
        await Wait(() => { var p = payment.TransformToVisual(filter).TransformPoint(new Point()); return filter.HorizontalOffset > 0 && p.X >= -1 && p.X + payment.ActualWidth <= filter.ViewportWidth + 1; });
        var position = payment.TransformToVisual(filter).TransformPoint(new Point());
        Check(position.X >= -1 && position.X + payment.ActualWidth <= filter.ViewportWidth + 1, "標準の次フォーカス移動で画面外の支払方法が表示範囲へ移動");
        var peer = new ScrollViewerAutomationPeer(filter); var scrollPattern = (IScrollProvider?)peer.GetPattern(PatternInterface.Scroll);
        Check(scrollPattern is { HorizontallyScrollable: true } && AutomationProperties.GetName(filter) == "記録の共通フィルター", "共通フィルターは標準UIAスクロールと操作名を公開");
        window.AppWindow.ResizeClient(new SizeInt32(1100, 780)); await Settle();
        var month = Find<TextBox>(root, "ReceiptMonth"); var keyword = Find<TextBox>(root, "RecordKeyword");
        var current = DateTime.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        month.Text = "bad"; await InvokeAsync(() => Find<Button>(root, "RecordMonthApply"));
        Check(transfers.Count == 1 && receipts.Count == 1 && Find<TextBlock>(root, "ReceiptMonthError").Visibility == Visibility.Visible, "不正共通年月は両APIを呼ばず近傍エラー");
        month.Text = "2027-01"; keyword.Text = "未確定";
        payment.SelectedItem = payment.Items.Cast<ReceiptFilterChoice>().Single(v => v.Id == masters.ActiveId);
        await Wait(() => window.ReceiptHomeReady && transfers.Count == 2 && receipts.Count == 2);
        owner.SelectedItem = owner.Items.Cast<ReceiptFilterChoice>().Single(v => v.Id == masters.Other);
        await Wait(() => window.ReceiptHomeReady && transfers.Count == 3 && receipts.Count == 3);
        var values = TransferQueryValues(transfers[^1]);
        Check(values["month"] == current && values["q"] == "" && values["ownerPersonId"] == masters.Other.ToString("D") && values["paymentMethodId"] == masters.ActiveId.ToString("D") && ((ReceiptFilterChoice)payment.SelectedItem).Id == masters.ActiveId,
            "人物・支払方法は独立ANDで両一覧へ適用し未確定年月や名前を確定しない");
        await InvokeAsync(() => Find<Button>(root, "RecordMonthApply")); await Wait(() => window.ReceiptHomeReady && transfers.Count == 4 && receipts.Count == 4);
        Check(TransferQueryValues(transfers[^1])["month"] == "2027-01" && TransferQueryValues(transfers[^1])["q"] == "" && TransferQueryValues(receipts[^1])["q"] == "" && keyword.Text == "未確定",
            "共通年月だけの確定はキーワード下書きを確定しない");
        await InvokeAsync(() => Find<Button>(root, "RecordKeywordSearch"));
        await Wait(() => window.ReceiptHomeReady && receipts.Count == 5 && transfers.Count == 5);
        Check(TransferQueryValues(receipts[^1])["q"] == "未確定" && TransferQueryValues(transfers[^1])["q"] == "未確定",
            "共通キーワードの確定は店名と振替名の両方へ適用して各一覧を再取得");
        Find<RadioButton>(root, "ReceiptAllPeriods").IsChecked = true; await Wait(() => window.ReceiptHomeReady && transfers.Count == 6 && receipts.Count == 6);
        Check(TransferQueryValues(transfers[^1])["month"] == "all" && !month.IsEnabled, "全期間は両一覧へ共通適用し年月操作を無効化");
        Find<RadioButton>(root, "RecordMonthPeriod").IsChecked = true; await Wait(() => window.ReceiptHomeReady && transfers.Count == 7 && receipts.Count == 7);
        Check(TransferQueryValues(transfers[^1])["month"] == "2027-01" && ((ReceiptFilterChoice)owner.SelectedItem).Id == masters.Other, "月指定復帰は保持年月・人物・支払方法を維持");
        count = 101; await InvokeAsync(() => Find<Button>(root, "RecordKeywordSearch")); await Wait(() => list.Items.Count == 100 && window.TransferHomeReady);
        var first = (Monetail.Windows.Presentation.Transfers.TransferListItem)list.Items[0]; list.SelectedItem = first;
        await Wait(() => All(list).OfType<TextBlock>().Any(t => t.Text == "振替0"));
        Check(first.FromName == "本日有効" && first.ToName == "（母）母の現金" && first.TransactionAt == "2026/11/10 12:34:56" && first.Amount == 12345.ToString("N0", CultureInfo.CurrentCulture) && first.Memo == "", "振替6列は元表記・保存額・本人/他人物の支払方法名を表示");
        var state = Find<Monetail.WinUI.Views.Transfers.TransferListView>(root, "TransfersView").ViewModel!;
        var offset = filter.HorizontalOffset; list.ScrollIntoView(list.Items[79]); await Settle();
        Check(filter.HorizontalOffset == offset, "振替一覧の縦スクロールは共通フィルターから独立");
        fail = true; await state.ObserveViewportAsync(79, true, true); await Wait(() => Find<StackPanel>(root, "TransferNextFailure").Visibility == Visibility.Visible);
        var failures = transfers.Count; await Settle(); Check(transfers.Count == failures && list.Items.Count == 100 && ReferenceEquals(list.SelectedItem, first), "追加取得失敗は行・選択を保持し自動再試行しない");
        await InvokeAsync(() => Find<Button>(root, "TransferNextRetry")); await Wait(() => list.Items.Count == 101 && !state.IsLoading);
        Check(ReferenceEquals(list.SelectedItem, first), "追加ページの明示再試行は選択を維持して末尾へ追記");
        await InvokeAsync(() => Find<Button>(root, "RecordKeywordSearch")); await Wait(() => list.Items.Count == 100 && !state.IsLoading);
        invalidCursor = true; var beforeRecovery = transfers.Count; await state.ObserveViewportAsync(79, true, true);
        await Wait(() => !state.IsLoading && transfers.Count == beforeRecovery + 2);
        Check(list.Items.Count == 100 && state.Failure is null && TransferQueryValues(transfers[^1])["month"] == "2027-01" && !TransferQueryValues(transfers[^1]).ContainsKey("cursor"), "invalid_cursorは同じ条件で先頭を1回だけ自動再取得");
        fail = true; await InvokeAsync(() => Find<Button>(root, "RecordKeywordSearch")); await Wait(() => state.IsInitialFailure);
        Check(Find<StackPanel>(root, "TransferInitialFailure").Visibility == Visibility.Visible && Find<Button>(root, "SettingsButton").IsEnabled, "振替初回失敗は中央で再試行でき他操作を塞がない");
        count = 1; await InvokeAsync(() => Find<Button>(root, "TransferInitialRetry")); await Wait(() => list.Items.Count == 1 && !state.IsLoading);
        root.RequestedTheme = ElementTheme.Dark; await Settle(); Check(root.ActualTheme == ElementTheme.Dark && list.Items.Count == 1, "振替一覧と共通フィルターは標準ダークテーマで状態を保持"); root.RequestedTheme = ElementTheme.Default;
        Check(fixture.Requests.Where(p => p.Contains("/transfers", StringComparison.Ordinal)).All(p => p.EndsWith("/transfers", StringComparison.Ordinal)), "この工程は振替一覧GETだけを接続し詳細・書込を呼ばない");
        fixture.TransfersHandler = (_, _) => Task.FromResult(PaymentServer.Json(new { code = "not_found", field = (string?)null, latest = (object?)null, permissions = new { currentRole = (string?)null, myPersonId = (Guid?)null } }, HttpStatusCode.NotFound));
        await InvokeAsync(() => Find<Button>(root, "RecordKeywordSearch")); await Wait(() => state.IsInitialFailure); var missingReads = fixture.Requests.Count;
        await Settle(); Check(Find<TextBlock>(root, "TransferInitialError").Text.Contains("参加状態") && fixture.Requests.Count == missingReads, "振替のアクセス喪失は専用メッセージで自動再試行しない");
        fixture.RejectSession = true; await InvokeAsync(() => Find<Button>(root, "TransferInitialRetry")); await Wait(() => Visible(root, "LoginPage"));
        var stoppedReads = fixture.Requests.Count; await Settle(); Check(list.Items.Count == 0 && fixture.Requests.Count == stoppedReads, "振替401は既存ログインへ戻り一覧を破棄し無限再試行しない");
    }
}
