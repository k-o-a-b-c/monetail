using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Client.Receipts;
using Windows.Graphics;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private static HttpResponseMessage TaxHistoryResponse() => PaymentServer.Json(new { rates = new[] {
        new { taxCategoryCode = "Standard", validFrom = "2026-11-10", validTo = (string?)"2026-11-10", rate = (string?)"10.00" },
        new { taxCategoryCode = "Standard", validFrom = "2026-11-11", validTo = (string?)null, rate = (string?)"1" },
        new { taxCategoryCode = "ReducedFood", validFrom = "2026-11-10", validTo = (string?)null, rate = (string?)"8.00" },
        new { taxCategoryCode = "ReducedNewspaper", validFrom = "2026-11-10", validTo = (string?)null, rate = (string?)"8" },
        new { taxCategoryCode = "ReducedFood", validFrom = "2026-11-09", validTo = (string?)"2026-11-09", rate = (string?)"0" }
    } });
    private static void SelectReceiptTax(ComboBox box, decimal? rate) => box.SelectedItem = box.Items.OfType<ComboBoxItem>()
        .Single(i => i.Tag is ReceiptTaxSelection { IsSelected: true } t && t.Rate == rate);

    private async Task CheckReceiptTaxCalculation()
    {
        using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true };
        var categories = new CategoryServer();
        for (var i = 0; i < categories.Rows.Count; i++) categories.Rows[i] = categories.Rows[i] with { TaxCategoryCode = "Standard" };
        fixture.CategoriesHandler = categories.HandleAsync;
        var payments = new PaymentServer(); fixture.PaymentsHandler = payments.HandleAsync;
        fixture.PeopleHandler = (_, _) => Task.FromResult(PaymentServer.Json(payments.People()));
        fixture.TaxRatesHandler = (_, _) => Task.FromResult(TaxHistoryResponse());
        var writes = 0; fixture.ReceiptsHandler = (request, _) => { if (request.Method != HttpMethod.Get) writes++; return Task.FromResult(PaymentServer.Json(new { receipts = Array.Empty<object>(), nextCursor = (string?)null })); };
        var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync(); await window!.OpenReceiptEditorAsync(null);
        await Wait(() => Find<Button>(root, "ReceiptBack").IsLoaded);
        var date = Find<TextBox>(root, "ReceiptDate"); date.Text = "2026/11/10";
        Find<TextBox>(root, "ReceiptShopName").Text = "計算確認";
        Find<RadioButton>(root, "ReceiptTaxExcluded").IsChecked = true;
        Find<ComboBox>(root, "ReceiptCategoryChoice").SelectedIndex = 0;
        await InvokeAsync(() => Find<Button>(root, "ReceiptCategoryAdd"));
        var editor = ReceiptEditing!; var tax = Find<ComboBox>(root, "ReceiptItemTax");
        Check(tax.Items.OfType<ComboBoxItem>().Select(i => (ReceiptTaxSelection)i.Tag).SequenceEqual(new[] {
            ReceiptTaxSelection.Exempt, ReceiptTaxSelection.Taxed(8), ReceiptTaxSelection.Taxed(10) }) &&
            editor.Draft!.Tax == ReceiptTaxSelection.Taxed(10) && ((ComboBoxItem)tax.SelectedItem).Content!.ToString() == "10%",
            "全区分の税率候補は非課税と数値昇順・同率除去、初期税率は科目と取引日、末尾ゼロ省略");
        var name = Find<TextBox>(root, "ReceiptItemName"); var price = Find<TextBox>(root, "ReceiptItemPrice");
        name.Text = "A"; price.Text = "19"; await InvokeAsync(() => Find<Button>(root, "ReceiptDetailApply"));
        name.Text = "B"; price.Text = "19"; await InvokeAsync(() => Find<Button>(root, "ReceiptDetailApply"));
        var group = editor.Groups[0];
        Check(editor.Draft!.Tax == ReceiptTaxSelection.Taxed(10) && Find<TextBlock>(root, "ReceiptAccountingTotal").Text.EndsWith("41") &&
            Find<ListView>(root, "ReceiptDetailsList").Items.OfType<ReceiptDetailRow>().All(r => r.ReferenceAmount == "20") &&
            Find<ListView>(root, "ReceiptCategories").Items.OfType<ReceiptCategoryRow>().Single().ReferenceAmount.EndsWith("41"),
            "正式表示は共通計算の明細20円ずつ・グループ41円・会計41円、次の新規も科目初期値");
        var rows = Find<ListView>(root, "ReceiptDetailsList"); rows.SelectedIndex = 0;
        await Wait(() => editor.SelectedDetailKey == group.Details[0].Key);
        var selected = editor.SelectedDetailKey; var draft = editor.Draft; var rowObjects = rows.Items.Cast<object>().ToArray();
        price.Text = "不正な生文字列";
        Check(Find<TextBlock>(root, "ReceiptAccountingTotal").Text.EndsWith("41") && group.Details.Count == 2 &&
            editor.SelectedDetailKey == selected && ReferenceEquals(draft, editor.Draft) && rows.Items.Cast<object>().SequenceEqual(rowObjects),
            "未反映の不正文字列は正式合計へ入れず、再計算で行・選択・下書きを再生成しない");
        date.Text = "2026/11/09";
        Check(tax.Items.OfType<ComboBoxItem>().Any(i => i.Tag is ReceiptTaxSelection { IsSelected: true, Rate: 0m }) &&
            tax.Items.OfType<ComboBoxItem>().Any(i => i.Tag is ReceiptTaxSelection { IsSelected: true, Rate: null }),
            "課税0%と非課税を異なる候補として表示");
        date.Text = "2026/11/11";
        Check(((ReceiptTaxSelection)((ComboBoxItem)tax.SelectedItem).Tag).Rate == 10 &&
            ((ComboBoxItem)tax.SelectedItem).Content!.ToString()!.Contains("要再選択") && price.Text == "不正な生文字列" &&
            rows.Items.OfType<ReceiptDetailRow>().All(r => r.TaxWarning == "要再選択" && r.ReferenceAmount == "未計算") &&
            Find<TextBlock>(root, "ReceiptAccountingTotal").Text.EndsWith("未計算"), "日付変更は全明細を照合し期限外値・下書きを保持、行とフォームで再選択を表示");
        date.Text = "未確定";
        Check(Find<TextBlock>(root, "ReceiptAccountingTotal").Text.EndsWith("未計算") && rows.Items.OfType<ReceiptDetailRow>().All(r => r.TaxWarning == "未判定"),
            "未確定日付では前の有効日付を現在の確定結果として表示しない");
        date.Text = "2026/11/10";
        Check(Find<TextBlock>(root, "ReceiptAccountingTotal").Text.EndsWith("41") && fixture.TaxRatesRequests == 1 &&
            group.Details.All(d => d.Values.Tax == ReceiptTaxSelection.Taxed(10)) && price.Text == "不正な生文字列",
            "日付を戻すと反映済みの正式額を復元し、追加GET・税率書換え・下書き破棄なし");
        window.AppWindow.ResizeClient(new SizeInt32(780, 650)); await Settle();
        Check(Find<TextBlock>(root, "ReceiptAccountingTotal").ActualWidth > 0 && Find<ComboBox>(root, "ReceiptItemTax").ActualWidth > 0 &&
            new[] { "ReceiptRegister", "ReceiptUpdate", "ReceiptReuse" }.All(n => !Find<Button>(root, n).IsEnabled) && writes == 0,
            "狭幅でも会計合計と税率に到達でき、登録/更新/流用登録は無効で保存APIを呼ばない");
        await InvokeAsync(() => Find<Button>(root, "ReceiptBack")); await ConfirmReceipt(root, true);
        await Wait(() => ReceiptEditing is null); await window.OpenReceiptEditorAsync(null);
        Check(fixture.TaxRatesRequests == 1 && !ReceiptEditing!.NeedsLeaveConfirmation, "再開時はログイン検証を経たセッション税率キャッシュを再利用し初期表示だけでは未変更");
    }

    private async Task CheckReceiptTaxLoading()
    {
        using (var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true })
        {
            var categories = new CategoryServer(); categories.Rows[0] = categories.Rows[0] with { TaxCategoryCode = "Exempt", ValidFrom = null, ValidTo = null };
            fixture.CategoriesHandler = categories.HandleAsync;
            var payments = new PaymentServer(); fixture.PaymentsHandler = payments.HandleAsync;
            fixture.TaxRatesHandler = (_, _) => Task.FromResult(fixture.TaxRatesRequests == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : PaymentServer.Json(new { rates = Array.Empty<object>() }));
            var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync(); await window!.OpenReceiptEditorAsync(null);
            Check(ReceiptEditing is null && Find<Button>(root, "ReceiptOpenRetry").Visibility == Visibility.Visible &&
                !Find<ProgressRing>(root, "ReceiptOpenProgress").IsActive && fixture.TaxRatesRequests == 1,
                "税率取得失敗は空履歴で代用せず、リング停止・明示再試行を表示し自動再送しない");
            await InvokeAsync(() => Find<Button>(root, "ReceiptOpenRetry")); await Wait(() => ReceiptEditing is not null);
            Check(fixture.TaxRatesRequests == 2, "税率取得の再試行は明示操作だけで実行");
            Find<ComboBox>(root, "ReceiptCategoryChoice").SelectedIndex = 0; await InvokeAsync(() => Find<Button>(root, "ReceiptCategoryAdd"));
            Check(ReceiptEditing!.Draft!.Tax == ReceiptTaxSelection.Exempt && Find<ComboBox>(root, "ReceiptItemTax").Items.Count == 1 &&
                !ReceiptEditing.HasUnappliedChanges, "履歴0件でもExempt科目は非課税を初期選択し、初期値だけでは下書き未変更");
        }
        using (var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true })
        {
            var categories = new CategoryServer(); fixture.CategoriesHandler = categories.HandleAsync;
            var payments = new PaymentServer(); fixture.PaymentsHandler = payments.HandleAsync;
            var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously); var cancelled = false;
            fixture.TaxRatesHandler = (_, token) => { token.Register(() => cancelled = true); return pending.Task; };
            var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync(); var opening = window!.OpenReceiptEditorAsync(null);
            await Wait(() => fixture.TaxRatesRequests == 1);
            await InvokeAsync(() => Find<Button>(root, "SettingsButton")); await Wait(() => Visible(root, "ManagementPage"));
            pending.SetResult(TaxHistoryResponse()); await opening;
            Check(cancelled && ReceiptEditing is null && !Find<ProgressRing>(root, "ReceiptOpenProgress").IsActive && Visible(root, "ManagementPage"),
                "税率取得中の離脱は中断し、取消を無視した旧応答も入力画面へ適用しない");
        }
        using (var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true })
        {
            var categories = new CategoryServer(); fixture.CategoriesHandler = categories.HandleAsync;
            var payments = new PaymentServer(); fixture.PaymentsHandler = payments.HandleAsync;
            fixture.TaxRatesHandler = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync(); await window!.OpenReceiptEditorAsync(null);
            await Wait(() => Visible(root, "LoginPage"));
            Check(ReceiptEditing is null && !Find<ProgressRing>(root, "ReceiptOpenProgress").IsActive,
                "税率取得の401は既存の認証更新/ログイン処理へ接続し、未取得のまま編集を開始しない");
        }

    }
}
