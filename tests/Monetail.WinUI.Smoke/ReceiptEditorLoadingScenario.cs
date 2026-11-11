using System.Globalization;
using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.WinUI.Input;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private async Task CheckReceiptEditorLoading()
    {
        using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true };
        var categories = new CategoryServer(); fixture.CategoriesHandler = categories.HandleAsync;
        var payments = new PaymentServer(); fixture.PeopleHandler = (_, _) => Task.FromResult(PaymentServer.Json(payments.People())); fixture.PaymentsHandler = payments.HandleAsync;
        var date = DateTime.SpecifyKind(DateTime.Today.AddDays(-1).AddHours(13).AddMinutes(24).AddSeconds(35), DateTimeKind.Unspecified);
        var summaries = Enumerable.Range(1, 100).Select(i => new ReceiptSummaryInfo(Guid.Parse(i.ToString("x32")), date, "保存店" + i, null, payments.ExpiredId, i, null)).ToArray();
        ReceiptInfo Detail(Guid id) => new(id, date, "保存店", "保存所在地", payments.ExpiredId, 1, "floor", 2, "Month", null, Guid.NewGuid(), [
            new(Guid.NewGuid(), categories.Rows[1].CategoryId, null, [new(Guid.NewGuid(), 0, "保存明細", 100, 1, 0, 0, null, "Excluded", "Regular", null, [])]),
            new(Guid.NewGuid(), categories.Rows[0].CategoryId, "保存科目メモ", [new(Guid.NewGuid(), 0, "別明細", -20, 2, 0, 0, 0, "Variable", "Irregular", null, [])])]);
        var reads = new List<Guid>(); var listReads = 0; var fail = false;
        TaskCompletionSource<bool>? gate = null; var cancelled = false;
        fixture.ReceiptsHandler = async (request, token) =>
        {
            if (request.Method != HttpMethod.Get) throw new InvalidOperationException("保存APIを呼んではいけません。");
            if (request.RequestUri!.AbsolutePath.EndsWith("/receipts")) { listReads++; return PaymentServer.Json(new ReceiptListInfo(summaries, null)); }
            var id = Guid.Parse(request.RequestUri.Segments[^1]); reads.Add(id);
            if (gate is { } waiting) { gate = null; using var registration = token.Register(() => cancelled = true); await waiting.Task; }
            if (fail) { fail = false; return new(HttpStatusCode.ServiceUnavailable); }
            return PaymentServer.Json(Detail(id));
        };
        var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync();
        var list = Find<ListView>(root, "ReceiptList");
        await Wait(() => list.Items.Count == 100);
        var selected = list.Items[79]; list.SelectedItem = selected;
        list.ScrollIntoView(selected); await Settle(); var scroll = All(list).OfType<ScrollViewer>().First(); var offset = scroll.VerticalOffset;
        Check(reads.Count == 0 && offset > 0, "一覧の単一選択は詳細GETを行わず、非先頭の表示位置を保持");
        // ダブルクリックのイベントと同じ共通入口を使用（物理ポインター操作とは区別）。
        await window!.OpenReceiptEditorAsync(summaries[79].ReceiptId); await Wait(() => Find<Button>(root, "ReceiptBack").ActualWidth > 0);
        var editor = ReceiptEditing!; var originalGroup = editor.Groups[0]; var originalDetail = originalGroup.Details[0];
        Check(reads.Count == 1 && editor.ReceiptId == summaries[79].ReceiptId && editor.Header.TransactionAt == date &&
            Find<TimeInputBox>(root, "ReceiptTime").Text == "13:24:35" && Find<DateInputBox>(root, "ReceiptDate").Text == date.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture),
            "既存編集は詳細GET一回で保存済み取引日時を再現し新規初期値を適用しない");
        Check(editor.Header.ShopName == "保存店" && editor.Header.ShopLocation == "保存所在地" && editor.Header.PaymentMethodId == payments.ExpiredId &&
            editor.Header.TaxInclusionMode == 1 && ((ComboBoxItem)Find<ComboBox>(root, "ReceiptTaxInclusion").SelectedItem).Content.ToString() == "税抜" &&
            editor.Header.TaxRoundingMode == "floor" && editor.Header.RecurrenceInterval == 2 && editor.Header.RecurrenceUnit == "Month" && editor.Header.Memo is null && !editor.NeedsLeaveConfirmation,
            "既存親情報・NULL・税区分・周期を保持し、開いただけでは未変更");
        Check(Find<Button>(root, "ReceiptRegister").Visibility == Visibility.Collapsed && Find<Button>(root, "ReceiptUpdate").Visibility == Visibility.Visible &&
            Find<Button>(root, "ReceiptReuse").Visibility == Visibility.Visible && !Find<Button>(root, "ReceiptUpdate").IsEnabled && !Find<Button>(root, "ReceiptReuse").IsEnabled,
            "既存画面は更新/流用登録だけを無効状態で表示し保存成功を偽装しない");
        window.ActivateReceiptCategory(originalGroup.Key);
        Check(!editor.NeedsLeaveConfirmation && originalGroup.Details[0].Key == originalDetail.Key && originalGroup.Details[0].DetailId == originalDetail.DetailId &&
            originalDetail.Values.Tax.IsSelected && originalDetail.Values.Tax.Rate is null, "科目選択でも保存済み明細ID・実税率とNULLメモを変更しない");
        await InvokeAsync(() => Find<Button>(root, "ReceiptBack")); await Wait(() => ReceiptEditing is null && Visible(root, "RecordContent")); await Settle();
        Check(ReferenceEquals(list.SelectedItem, selected) && Math.Abs(scroll.VerticalOffset - offset) < 1 && listReads == 1 && list.Items.Count == 100,
            "無変更編集から一覧へ戻ると追加GETなしで検索・選択・非先頭スクロールを保持");
        // 失敗は一覧を残し、明示的に開き直したときだけ再試行。
        fail = true; await window.OpenReceiptEditorAsync(summaries[0].ReceiptId); var failedReads = reads.Count; await Settle();
        Check(ReceiptEditing is null && Visible(root, "RecordContent") && Find<StackPanel>(root, "ReceiptOpenNotice").Visibility == Visibility.Visible &&
            !Find<ProgressRing>(root, "ReceiptOpenProgress").IsActive && reads.Count == failedReads, "詳細GET失敗は一覧を保持しリング停止・エラー表示、自動再試行しない");
        // サーバーが取消を無視して応答しても旧要求を反映しない。
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); gate = pending;
        var old = window.OpenReceiptEditorAsync(summaries[1].ReceiptId); await Wait(() => reads.Count == failedReads + 1);
        await window.OpenReceiptEditorAsync(summaries[2].ReceiptId);
        Check(reads.Count == failedReads + 1 && Find<ProgressRing>(root, "ReceiptOpenProgress").IsActive, "詳細GET中の二重起動は追加要求を送らない");
        await InvokeAsync(() => Find<Button>(root, "SettingsButton")); await Wait(() => Visible(root, "ManagementPage") && Find<ContentControl>(root, "Operations").IsEnabled);
        Check(cancelled && !Find<ProgressRing>(root, "ReceiptOpenProgress").IsActive, "詳細GETの画面離脱は要求を中断しリング停止");
        await InvokeAsync(() => Find<Button>(root, "ReturnButton")); await Wait(() => Visible(root, "HomePage"));
        await window.OpenReceiptEditorAsync(summaries[2].ReceiptId); await Wait(() => ReceiptEditing is not null);
        var newer = ReceiptEditing; pending.SetResult(true); await old;
        Check(ReferenceEquals(newer, ReceiptEditing) && ReceiptEditing!.ReceiptId == summaries[2].ReceiptId, "中断要求の遅延応答は新しい編集画面を上書きしない");
        await Wait(() => Find<Button>(root, "ReceiptBack").ActualWidth > 0); await InvokeAsync(() => Find<Button>(root, "ReceiptBack"));
        await Wait(() => ReceiptEditing is null);
        fixture.RejectSession = true; await window.OpenReceiptEditorAsync(summaries[0].ReceiptId);
        await Wait(() => Visible(root, "LoginPage"));
        Check(ReceiptEditing is null && !Find<ProgressRing>(root, "ReceiptOpenProgress").IsActive, "詳細GET中の認証失効は既存ログイン画面へ戻し編集を開始しない");
    }
}
