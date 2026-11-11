using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.WinUI.Input;
using Monetail.Windows.Client.Receipts;
using Windows.Graphics;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private async Task CheckReceiptEditorOperations()
    {
        using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true, ChoiceCount = 2 };
        var categories = new CategoryServer(); fixture.CategoriesHandler = categories.HandleAsync;
        var payments = new PaymentServer(); fixture.PeopleHandler = (_, _) => Task.FromResult(PaymentServer.Json(payments.People())); fixture.PaymentsHandler = payments.HandleAsync;
        var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync(); await window!.OpenReceiptEditorAsync(null);
        await Wait(() => Find<Button>(root, "ReceiptBack").ActualWidth > 0);
        var editor = ReceiptEditing!;
        var choices = Find<ComboBox>(root, "ReceiptCategoryChoice"); var groups = Find<ListView>(root, "ReceiptCategories");
        choices.SelectedIndex = 0; await InvokeAsync(() => Find<Button>(root, "ReceiptCategoryAdd"));
        await Wait(() => editor.Groups.Count == 1); var first = editor.Groups[0];
        Check(first.CategoryId == categories.Rows[0].CategoryId && editor.SelectedGroupKey == first.Key && editor.Draft!.Tax == ReceiptTaxSelection.Unselected &&
            !Find<Button>(root, "ReceiptCategoryAdd").IsEnabled, "科目追加は共通順・選択を反映し、履歴未取得税率は未選択、重複追加は不可");
        Find<TextBox>(root, "ReceiptCategoryMemo").Text = "最初の科目メモ";
        choices.SelectedIndex = 1; await InvokeAsync(() => Find<Button>(root, "ReceiptCategoryAdd"));
        await Wait(() => editor.Groups.Count == 2); var second = editor.Groups[1];
        // 別科目追加の直前でも、元のテキストを反映することを検証する。
        window.ActivateReceiptCategory(first.Key);
        Check(first.Memo == "最初の科目メモ" && editor.SelectedGroupKey == first.Key && Find<TextBox>(root, "ReceiptCategoryMemo").Text == "最初の科目メモ", "科目切替は他科目のメモと入力モデルを保持");
        groups.ScrollIntoView(groups.Items[1]); await Wait(() => groups.ContainerFromIndex(1) is ListViewItem { IsLoaded: true });
        ((ListViewItem)groups.ContainerFromIndex(1)).Focus(FocusState.Keyboard);
        Check(editor.SelectedGroupKey == first.Key && groups.SingleSelectionFollowsFocus == false, "科目行のフォーカス移動だけでは表示中の科目を変更しない");
        Check(window.ActivateFocusedReceiptCategory() && editor.SelectedGroupKey == second.Key, "Enterと共通の確定処理でフォーカス行の科目へ切り替える");
        await InvokeAsync(() => Find<Button>(root, "ReceiptCategoryRemove"));
        await Wait(() => Find<ContentDialog>(root, "ReceiptConfirmationDialog").IsLoaded);
        var dialog = Find<ContentDialog>(root, "ReceiptConfirmationDialog");
        Check(dialog.Content.ToString()!.Contains(categories.Rows[1].Name) && dialog.Content.ToString()!.Contains("明細0件"), "科目削除確認は名前と明細件数を表示");
        await ConfirmReceipt(root, false); Check(editor.Groups.Count == 2 && editor.SelectedGroupKey == second.Key, "科目削除取消は選択と内容を維持");
        await InvokeAsync(() => Find<Button>(root, "ReceiptCategoryRemove")); await ConfirmReceipt(root, true);
        Check(editor.Groups.Count == 1 && editor.SelectedGroupKey is null && groups.SelectedItem is null && !Find<Button>(root, "ReceiptCategoryRemove").IsEnabled &&
            !Find<TextBox>(root, "ReceiptCategoryMemo").IsEnabled, "科目削除後は自動選択せず、フォームと削除を無効化");
        Find<TextBox>(root, "ReceiptShopName").Text = "親情報"; Find<TextBox>(root, "ReceiptShopLocation").Text = "所在地"; Find<TextBox>(root, "ReceiptMemo").Text = "レシートメモ";
        var tax = Find<RadioButton>(root, "ReceiptTaxIncluded"); tax.IsChecked = true;
        await Wait(() => editor.Header.TaxInclusionMode == 0);
        Check(tax.Content.ToString() == "税込", "税込表示はAPIの0と一致");
        Find<RadioButton>(root, "ReceiptTaxExcluded").IsChecked = true; Find<RadioButton>(root, "ReceiptTaxHalfUp").IsChecked = true; Find<ComboBox>(root, "ReceiptEditorPayment").SelectedIndex = 0;
        Find<TextBox>(root, "ReceiptInterval").Text = "2"; Find<ComboBox>(root, "ReceiptUnit").SelectedIndex = 1;
        await Wait(() => editor.Header.RecurrenceInterval == 2 && editor.Header.TaxRoundingMode == "half_up" && editor.Header.PaymentMethodId is not null);
        Check(editor.Header.TaxInclusionMode == 1 && Find<RadioButton>(root, "ReceiptTaxExcluded").IsChecked == true && editor.Header.RecurrenceUnit == "Month" &&
            editor.Header.ShopName == "親情報" && editor.Header.ShopLocation == "所在地" && editor.Header.Memo == "レシートメモ", "親情報・税設定・周期の変更をHeaderへ反映");
        var time = Find<TimeInputBox>(root, "ReceiptTime"); time.Text = "９１５"; time.Complete();
        var date = Find<DateInputBox>(root, "ReceiptDate"); date.Text = "20260401"; date.Complete();
        await Wait(() => editor.Header.TransactionAt == new DateTime(2026, 4, 1, 9, 15, 0));
        Check(time.Text == "09:15:00" && date.Text == "2026/04/01", "日付・時刻は既存Client補完とローカル秒精度で反映");
        window.AppWindow.ResizeClient(new SizeInt32(780, 650)); await Settle();
        Check(Grid.GetRow(Find<TimeInputBox>(root, "ReceiptTime")) == 1 && Grid.GetRow(Find<TextBox>(root, "ReceiptMemo")) == 1 &&
            Grid.GetRow(Find<StackPanel>(root, "ReceiptDetailsArea")) == 1, "狭幅では親項目・科目/明細領域を縦積みし項目を隠さない");
        // 入力途中の日付を、管理画面へ移るキャンセルで補正・破棄しない。
        date.Focus(FocusState.Programmatic); date.Text = "途中";
        await InvokeAsync(() => Find<Button>(root, "SettingsButton")); await ConfirmReceipt(root, false);
        Check(ReferenceEquals(editor, ReceiptEditing) && date.Text == "途中" && Visible(root, "ReceiptEditorPage"), "管理画面への離脱取消は未確定日付と編集内容を保持");
        await InvokeAsync(() => Find<Button>(root, "LedgerSelector")); await ConfirmReceipt(root, false);
        Check(ReferenceEquals(editor, ReceiptEditing) && Visible(root, "ReceiptEditorPage"), "家計簿切替入口でも明示破棄前には移動しない");
        await InvokeAsync(() => Find<Button>(root, "SettingsButton")); await ConfirmReceipt(root, true);
        await Wait(() => Visible(root, "ManagementPage") && Find<ContentControl>(root, "Operations").IsEnabled);
        Check(ReceiptEditing is null, "管理画面への明示破棄後だけ編集状態を終了する");
        Find<NavigationView>(root, "ManagementNavigation").SelectedItem = Find<NavigationViewItem>(root, "LogoutNavigation");
        await Wait(() => Visible(root, "LoginPage")); Check(ReceiptEditing is null, "離脱確認後の既存ログアウトを維持");
    }
}
