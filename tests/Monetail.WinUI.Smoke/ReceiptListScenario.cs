using System.Globalization;
using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private async Task CheckReceiptList()
    {
        using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true, ChoiceCount = 2 };
        var masters = new PaymentServer(); var requests = new List<Uri>(); var fail = false; var count = 0;
        fixture.PeopleHandler = (_, _) => Task.FromResult(PaymentServer.Json(masters.People()));
        fixture.PaymentsHandler = masters.HandleAsync;
        fixture.ReceiptsHandler = (request, _) =>
        {
            requests.Add(request.RequestUri!);
            if (fail) { fail = false; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); }
            var offset = request.RequestUri!.Query.Contains("cursor=next", StringComparison.Ordinal) ? 100 : 0;
            var rows = Enumerable.Range(offset, Math.Min(100, Math.Max(0, count - offset))).Select(i => new ReceiptSummaryInfo(
                Guid.Parse((i + 1).ToString("x32")), new(2026, 11, 10, 12, 34, 56), "店" + i, i == 0 ? null : "所在地",
                i % 2 == 0 ? masters.ActiveId : masters.ExpiredId, -12345, null)).ToArray();
            return Task.FromResult(PaymentServer.Json(new ReceiptListInfo(rows, count > offset + 100 ? "next" : null)));
        };
        var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync(); await Wait(() => window.ReceiptHomeReady);
        var list = Find<ListView>(root, "ReceiptList");
        Check(Find<TextBlock>(root, "ReceiptEmpty").Visibility == Visibility.Visible && requests.Count == 1, "0件は中央表示で追加要求しない");
        var month = Find<TextBox>(root, "ReceiptMonth"); var shop = Find<TextBox>(root, "ReceiptShop");
        var current = DateTime.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        month.Text = "invalid"; shop.Text = "入力途中"; await InvokeAsync(() => Find<Button>(root, "ReceiptSearchButton"));
        Check(requests.Count == 1 && Find<TextBlock>(root, "ReceiptMonthError").Visibility == Visibility.Visible, "不正年月はAPIを呼ばず入力欄で案内");
        month.Text = "2026-12";
        Find<ComboBox>(root, "ReceiptOwner").SelectedIndex = 1; await Wait(() => window.ReceiptHomeReady && requests.Count == 2);
        Check(requests[^1].Query.Contains("month=" + current) && !requests[^1].Query.Contains(Uri.EscapeDataString("入力途中")) && month.Text == "2026-12" && shop.Text == "入力途中", "選択変更は確定済み条件を使用し年月と店名の下書きを保持");
        count = 101; await InvokeAsync(() => Find<Button>(root, "ReceiptSearchButton")); await Wait(() => window.ReceiptHomeReady && list.Items.Count == 100);
        Check(requests[^1].Query.Contains("month=2026-12") && requests[^1].Query.Contains(Uri.EscapeDataString("入力途中")), "検索ボタンで年月と店名を一括確定");
        var first = (ReceiptRow)list.Items[0]; list.SelectedItem = first;
        Check(first.TransactionAt == "2026/11/10 12:34:56" && first.TotalAmount == (-12345).ToString("N0", CultureInfo.CurrentCulture) && first.ShopLocation == "" && first.Memo == "", "ローカル秒精度・NULLと保存済み合計額を表示し再計算しない");
        Check(first.PaymentName == "本日有効" && ((ReceiptRow)list.Items[1]).PaymentName == "終了済み", "過去の期間外支払方法も名称表示できる");
        var state = (ReceiptListState)typeof(MainWindow).GetField("receiptState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
        list.ScrollIntoView(list.Items[99]); await Settle();
        await state.ObserveViewportAsync(99, true, true); await Wait(() => list.Items.Count == 101);
        Check(ReferenceEquals(list.SelectedItem, first), "追加読込は既存行と選択を維持");
        var cache = ((ClientConnection)typeof(MainWindow).GetField("connection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(window)!).Masters;
        var ledger = ((LedgerInfo)typeof(MainWindow).GetField("currentLedger", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(window)!).LedgerId;
        var reads = masters.Reads; var histories = requests.Count;
        await InvokeAsync(() => Find<Button>(root, "SettingsButton"));
        await Wait(() => Visible(root, "ManagementPage") && Find<ContentControl>(root, "Operations").IsEnabled);
        masters.Rows[0] = masters.Rows[0] with { Name = "変更後の支払方法" };
        Find<NavigationView>(root, "ManagementNavigation").SelectedItem = Find<NavigationViewItem>(root, "PaymentsNavigation");
        await Wait(() => masters.Reads == reads + 1 && Find<ListView>(root, "PaymentsList").Items.Count == 2 && Find<ContentControl>(root, "Operations").IsEnabled);
        await InvokeAsync(() => Find<Button>(root, "ReturnButton"));
        await Wait(() => Visible(root, "HomePage"));
        Check(first.PaymentName == "変更後の支払方法" && requests.Count == histories && list.Items.Count == 101 && ReferenceEquals(list.SelectedItem, first), "管理画面の再取得をキャッシュへ反映し、記録へ戻っても追加GETせず一覧・選択を維持");
        fail = true; await InvokeAsync(() => Find<Button>(root, "ReceiptSearchButton"));
        await Wait(() => Find<StackPanel>(root, "ReceiptInitialFailure").Visibility == Visibility.Visible);
        var failedReads = requests.Count; await Settle(); Check(requests.Count == failedReads && Find<Button>(root, "SettingsButton").IsEnabled, "初回エラーは自動再試行せず他操作を妨げない");
        count = 1; await InvokeAsync(() => Find<Button>(root, "ReceiptInitialRetry")); await Wait(() => list.Items.Count == 1 && window.ReceiptHomeReady);
        Check(Find<StackPanel>(root, "ReceiptInitialFailure").Visibility == Visibility.Collapsed, "明示再試行で初回エラーから復帰");
        // 本人・接続先を変える操作は既存の実画面操作を通し、旧キャッシュが残らないことを確認する。
        await InvokeAsync(() => Find<Button>(root, "SettingsButton")); await Wait(() => Find<ContentControl>(root, "Operations").IsEnabled);
        Find<NavigationView>(root, "ManagementNavigation").SelectedItem = Find<NavigationViewItem>(root, "LogoutNavigation");
        await Wait(() => Visible(root, "LoginPage"));
        Check(cache.People(ledger) is null && cache.Payments(ledger) is null && cache.Categories(ledger) is null && list.Items.Count == 0, "ログアウトで全マスタと取得済みレシートを破棄");
    }
}
