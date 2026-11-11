using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Monetail.Windows.Client.Receipts;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private async Task CheckReceiptDetails()
    {
        using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true };
        var categories = new CategoryServer(); fixture.CategoriesHandler = categories.HandleAsync;
        var payments = new PaymentServer(); fixture.PeopleHandler = (_, _) => Task.FromResult(PaymentServer.Json(payments.People())); fixture.PaymentsHandler = payments.HandleAsync;
        var writes = 0; fixture.ReceiptsHandler = (request, _) => { if (request.Method != HttpMethod.Get) writes++; return Task.FromResult(PaymentServer.Json(new { receipts = Array.Empty<object>(), nextCursor = (string?)null })); };
        var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync(); await window!.OpenReceiptEditorAsync(null);
        await Wait(() => Find<Button>(root, "ReceiptBack").ActualWidth > 0);
        Find<ComboBox>(root, "ReceiptCategoryChoice").SelectedIndex = 0; await InvokeAsync(() => Find<Button>(root, "ReceiptCategoryAdd"));
        await Wait(() => ReceiptEditing!.Groups.Count == 1 && Find<Button>(root, "ReceiptDetailApply").IsLoaded);
        var editor = ReceiptEditing!; var group = editor.Groups[0];
        var name = Find<TextBox>(root, "ReceiptItemName"); var price = Find<TextBox>(root, "ReceiptItemPrice"); var tax = Find<ComboBox>(root, "ReceiptItemTax");
        Check(Find<TextBox>(root, "ReceiptItemQuantity").Text == "1" && Find<TextBox>(root, "ReceiptItemDiscount").Text == "0" &&
            Find<TextBox>(root, "ReceiptItemRateDiscount").Text == "0" && tax.SelectedIndex == -1 && editor.Draft!.Tax == ReceiptTaxSelection.Unselected &&
            All(Find<StackPanel>(root, "ReceiptDetailPeople")).OfType<CheckBox>().All(c => c.IsChecked != true), "明細初期値・人物0人・税率未選択を維持");
        name.Text = "明細A"; price.Text = "不正"; tax.SelectedIndex = 0;
        await InvokeAsync(() => Find<Button>(root, "ReceiptDetailApply"));
        Check(group.Details.Count == 0 && price.Text == "不正" && name.Text == "明細A" && Find<TextBlock>(root, "ReceiptDetailError").Visibility == Visibility.Visible, "不正入力は0へ補正せず追加拒否し入力を維持");
        price.Text = "100+20*3";
        await InvokeAsync(() => Find<Button>(root, "ReceiptDetailApply"));
        await Wait(() => group.Details.Count == 1);
        Check(group.Details[0].Values.UnitPrice == 160 && group.Details[0].DetailId is null && editor.SelectedDetailKey is null && name.Text == "" &&
            ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), name), "追加は画面内だけ・ID未採番、成功後は選択解除と品名フォーカス");
        var list = Find<ListView>(root, "ReceiptDetailsList"); list.SelectedIndex = 0;
        await Wait(() => editor.SelectedDetailKey == group.Details[0].Key);
        Check(name.Text == "明細A" && price.Text == "160" && tax.SelectedIndex == 0 && !editor.HasUnappliedChanges, "単一選択で保存した値を復元し更新モード");
        name.Text = "更新A"; await InvokeAsync(() => Find<Button>(root, "ReceiptDetailApply"));
        Check(group.Details.Count == 1 && group.Details[0].Values.Name == "更新A" && list.SelectedItem is null, "更新は同一行だけ変更し選択解除");
        // 人物は常時表示。フォーカスと選択は別で、連続入力へ選択を引き継ぐ。
        var people = Find<StackPanel>(root, "ReceiptDetailPeople").Children.OfType<CheckBox>().ToArray();
        people[0].IsChecked = true; name.Text = "B"; price.Text = "200"; tax.SelectedIndex = 0;
        await InvokeAsync(() => Find<Button>(root, "ReceiptDetailApply"));
        Check(group.Details.Count == 2 && group.Details[1].Values.PersonIds.Single() == (Guid)people[0].Tag &&
            Find<StackPanel>(root, "ReceiptDetailPeople").Children.OfType<CheckBox>().First().IsChecked == true, "追加後も人物集合を引継ぎ、他明細は変更しない");
        list.SelectedIndex = 0; await Wait(() => editor.SelectedDetailKey == group.Details[0].Key);
        var selected = editor.SelectedDetailKey; var draft = editor.Draft;
        price.Text = "途中"; name.Text = "並び替え中";
        await InvokeAsync(() => Find<Button>(root, "ReceiptDetailDown"));
        Check(editor.SelectedDetailKey == selected && ReferenceEquals(draft, editor.Draft) && group.Details[1].Key == selected &&
            price.Text == "途中" && name.Text == "並び替え中", "並び替えは同一対象と不正文字列を維持しフォームを再読込しない");
        list.SelectedIndex = 0; await ChooseReceiptDetailDraft(root, ContentDialogResult.None);
        Check(editor.SelectedDetailKey == selected && list.SelectedIndex == 1 && price.Text == "途中" && name.Text == "並び替え中", "切替取消で元選択と全入力を保持");
        list.SelectedIndex = 0; await ChooseReceiptDetailDraft(root, ContentDialogResult.Primary);
        Check(editor.SelectedDetailKey == selected && price.Text == "途中" && group.Details[1].Values.Name == "更新A" && Find<TextBlock>(root, "ReceiptDetailError").Visibility == Visibility.Visible, "追加/更新して移動の検証失敗では移動も元行変更もしない");
        list.SelectedIndex = 0; await ChooseReceiptDetailDraft(root, ContentDialogResult.Secondary);
        await Wait(() => editor.SelectedDetailKey == group.Details[0].Key);
        Check(name.Text == "B" && group.Details[1].Values.Name == "更新A", "破棄して移動は未反映入力だけを破棄し登録済み明細を維持");
        list.SelectedIndex = 0; await Settle();
        Check(!Find<ContentDialog>(root, "ReceiptConfirmationDialog").IsLoaded && !editor.HasUnappliedChanges, "フォーム未変更の再選択では確認を出さない");
        name.Text = "B更新"; list.SelectedIndex = 1; await ChooseReceiptDetailDraft(root, ContentDialogResult.Primary);
        Check(group.Details[0].Values.Name == "B更新" && editor.SelectedDetailKey == group.Details[1].Key, "更新して移動は元の行を更新後に選択先へ移動");
        price.Text = "3+"; Find<TextBox>(root, "ReceiptItemMemo").Text = "流用メモ";
        await InvokeAsync(() => Find<Button>(root, "ReceiptDetailReuse"));
        Check(editor.SelectedDetailKey is null && list.SelectedItem is null && price.Text == "3+" && Find<TextBox>(root, "ReceiptItemMemo").Text == "流用メモ" &&
            group.Details.Count == 2 && group.Details[1].Values.Memo is null, "流用は不正文字列を含む入力全体を保持し元の行や件数を変えない");
        price.Text = "300"; await InvokeAsync(() => Find<Button>(root, "ReceiptDetailApply"));
        Check(group.Details.Count == 3 && group.Details[2].Values.Memo == "流用メモ", "流用後の追加だけで新行を作成");
        list.SelectedIndex = 1; await Wait(() => editor.SelectedDetailKey is not null);
        await InvokeAsync(() => Find<Button>(root, "ReceiptDetailDelete"));
        Check(group.Details.Count == 2 && editor.SelectedDetailKey is null && list.SelectedItem is null && !Find<Button>(root, "ReceiptDetailDelete").IsEnabled, "削除後は必ず選択解除し次の行を自動選択しない");
        // 無効な削除ボタンの実装メソッドも再実行し、連続操作で別明細まで消えないことを確認。
        typeof(MainWindow).GetMethod("DeleteReceiptDetail", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, [null, new RoutedEventArgs()]);
        Check(group.Details.Count == 2, "削除の連続操作でも2件目は消えない");
        var categoryChoice = Find<ComboBox>(root, "ReceiptCategoryChoice"); categoryChoice.SelectedIndex = 1;
        Find<StackPanel>(root, "ReceiptDetailPeople").Children.OfType<CheckBox>().First().IsChecked = true;
        name.Text = "切替前"; price.Text = "7"; tax.SelectedIndex = 0;
        await InvokeAsync(() => Find<Button>(root, "ReceiptCategoryAdd")); await ChooseReceiptDetailDraft(root, ContentDialogResult.None);
        Check(editor.Groups.Count == 1 && name.Text == "切替前", "科目追加の下書き確認取消では追加先へ移動しない");
        await InvokeAsync(() => Find<Button>(root, "ReceiptCategoryAdd")); await ChooseReceiptDetailDraft(root, ContentDialogResult.Primary);
        await Wait(() => editor.Groups.Count == 2);
        Check(group.Details.Count == 3 && group.Details[2].Values.Name == "切替前" && editor.SelectedGroupKey == editor.Groups[1].Key &&
            ((ComboBoxItem)Find<ComboBox>(root, "ReceiptItemExpense").SelectedItem).Tag.ToString() == "Excluded" &&
            Find<StackPanel>(root, "ReceiptDetailPeople").Children.OfType<CheckBox>().First().IsChecked == true, "科目追加は元明細を反映後に移動し収入の初期費用区分を表示");
        var second = editor.Groups[1]; name.Text = "削除前"; price.Text = "途中";
        await InvokeAsync(() => Find<Button>(root, "ReceiptCategoryRemove")); await ChooseReceiptDetailDraft(root, ContentDialogResult.Secondary);
        await Wait(() => Find<ContentDialog>(root, "ReceiptConfirmationDialog").Title?.ToString() == "科目を削除");
        await ConfirmReceipt(root, false);
        Check(editor.Groups.Count == 2 && editor.SelectedGroupKey == second.Key && name.Text == "", "科目削除は下書き解決後に別の削除確認、取消では科目を保持");
        window.ActivateReceiptCategory(group.Key); await Wait(() => editor.SelectedGroupKey == group.Key);
        Check(group.Details.Count == 3 && list.Items.Count == 3 && name.Text == "", "科目切替後も他科目の登録済み明細を保持し新規フォームに戻る");
        name.Text = "科目切替中"; price.Text = "途中";
        window.ActivateReceiptCategory(second.Key); await ChooseReceiptDetailDraft(root, ContentDialogResult.None);
        Check(editor.SelectedGroupKey == group.Key && name.Text == "科目切替中" && price.Text == "途中", "別科目への切替取消も選択/入力を保護");
        window.ActivateReceiptCategory(second.Key); await ChooseReceiptDetailDraft(root, ContentDialogResult.Secondary);
        Check(editor.SelectedGroupKey == second.Key && group.Details.Count == 3 && name.Text == "", "別科目への破棄移動は元科目の登録済み明細を保持");
        window.ActivateReceiptCategory(group.Key); await Wait(() => editor.SelectedGroupKey == group.Key);
        await CheckReceiptDetailKeyboard(root);
        Check(writes == 0 && !Find<Button>(root, "ReceiptRegister").IsEnabled, "全体保存APIは呼ばず保存ボタンも無効");
        await InvokeAsync(() => Find<Button>(root, "ReceiptBack")); await ConfirmReceipt(root, true);
        await Wait(() => ReceiptEditing is null);
    }
    private async Task ChooseReceiptDetailDraft(FrameworkElement root, ContentDialogResult choice)
    {
        var dialog = Find<ContentDialog>(root, "ReceiptConfirmationDialog");
        string Label() => choice == ContentDialogResult.Primary ? dialog.PrimaryButtonText : choice == ContentDialogResult.Secondary ? dialog.SecondaryButtonText : dialog.CloseButtonText;
        Button Footer() => All(dialog).OfType<Button>().First(b => Equals(b.Content, Label()) && b.IsLoaded && b.ActualWidth > 0);
        await Wait(() => dialog.IsLoaded && dialog.Title?.ToString() == "未反映の明細があります" && All(dialog).OfType<Button>().Any(b => Equals(b.Content, Label()) && b.IsLoaded && b.ActualWidth > 0));
        await InvokeAsync(Footer);
        await Wait(() => !(bool)typeof(MainWindow).GetField("receiptDialogOpen", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(window)! || dialog.Title?.ToString() != "未反映の明細があります");
    }

}
