using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        Check(group.Details.Count == 0 && price.Text == "不正" && name.Text == "明細A" && Visible(root, "ReceiptDetailError"), "不正入力は0へ補正せず追加拒否し入力を維持");
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
        Check(writes == 0 && !Find<Button>(root, "ReceiptRegister").IsEnabled, "全体保存APIは呼ばず保存ボタンも無効");
    }
}
