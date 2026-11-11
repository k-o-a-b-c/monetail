using System.Net;
using System.Net.Http.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Client.Receipts;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private async Task CheckReceiptStoredDetails()
    {
        using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true };
        var categories = new CategoryServer(); fixture.CategoriesHandler = categories.HandleAsync;
        var payments = new PaymentServer(); fixture.PaymentsHandler = payments.HandleAsync;
        fixture.PeopleHandler = (_, _) => Task.FromResult(PaymentServer.Json(payments.People() with { People = payments.People().People.Select(p => p.PersonId == payments.Other ? p with { ValidTo = payments.Today.AddDays(-1) } : p).ToArray() }));
        var id = Guid.NewGuid(); var groupId = Guid.NewGuid(); var detailId = Guid.NewGuid(); var revision = Guid.NewGuid(); var writes = 0;
        fixture.ReceiptsHandler = (request, _) =>
        {
            if (request.Method != HttpMethod.Get) { writes++; throw new InvalidOperationException("全体保存不可"); }
            if (request.RequestUri!.AbsolutePath.EndsWith("/receipts")) return Task.FromResult(PaymentServer.Json(new { receipts = Array.Empty<object>(), nextCursor = (string?)null }));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new {
                receiptId = id, transactionAt = DateTime.Today.ToString("yyyy-MM-dd") + "T00:00:00", shopName = "既存", shopLocation = (string?)null,
                paymentMethodId = payments.ActiveId, taxInclusionMode = 0, taxRoundingMode = "floor", recurrenceInterval = (int?)null, recurrenceUnit = (string?)null,
                memo = (string?)null, revision, groups = new[] { new { groupId, categoryId = categories.Rows[0].CategoryId, memo = (string?)null, details = new[] {
                    new { detailId, displayOrder = 4, name = "既存0%", unitPrice = 1234, quantity = 2, unitDiscountAmount = 10, discountRate = "2.5", taxRate = "0",
                        expenseType = "Excluded", occurrenceType = "Temporary", memo = (string?)null, personIds = new[] { payments.Other } },
                    new { detailId = Guid.NewGuid(), displayOrder = 9, name = "既存非課税", unitPrice = -30, quantity = 1, unitDiscountAmount = 0, discountRate = "0", taxRate = (string?)null,
                        expenseType = "Variable", occurrenceType = "Irregular", memo = (string?)null, personIds = Array.Empty<Guid>() }
                } } }
            }) });
        };
        var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync(); await window!.OpenReceiptEditorAsync(id);
        var editor = ReceiptEditing!; var group = editor.Groups.Single(); window.ActivateReceiptCategory(group.Key);
        var list = Find<ListView>(root, "ReceiptDetailsList"); list.SelectedIndex = 0;
        await Wait(() => editor.SelectedDetailKey == group.Details[0].Key);
        var tax = Find<ComboBox>(root, "ReceiptItemTax");
        Check(editor.Draft!.Tax == ReceiptTaxSelection.Taxed(0) && ((ComboBoxItem)tax.SelectedItem).Content.ToString() == "0%" &&
            ((ComboBoxItem)Find<ComboBox>(root, "ReceiptItemExpense").SelectedItem).Tag.ToString() == "Excluded" &&
            ((ComboBoxItem)Find<ComboBox>(root, "ReceiptItemOccurrence").SelectedItem).Tag.ToString() == "Temporary", "既存の課税0%・対象外・臨時をそのまま復元");
        Check(Find<StackPanel>(root, "ReceiptDetailPeople").Children.OfType<CheckBox>().Single(c => (Guid)c.Tag == payments.Other).IsChecked == true && !editor.NeedsLeaveConfirmation,
            "有効期間外の対象人物も維持し、開いて選択しただけでは未変更");
        var rows = list.Items.OfType<ReceiptDetailRow>().ToArray();
        Check(rows[0].Price == 1234.ToString("N0") && rows[0].Discount == "10円 / 2.5%" && rows[0].Tax == "0%" && rows[1].Tax == "非課税" && rows[1].Discount == "—", "一覧の3桁区切り・値引単位・非課税/0%を区別");
        Find<TextBox>(root, "ReceiptItemName").Text = "一時変更"; Find<TextBox>(root, "ReceiptItemName").Text = "既存0%";
        Check(!editor.NeedsLeaveConfirmation, "フォーム値を元に戻すと未反映変更を解消");
        await InvokeAsync(() => Find<Button>(root, "ReceiptDetailApply"));
        Check(editor.ReceiptId == id && editor.Revision == revision && group.GroupId == groupId && group.Details[0].DetailId == detailId && !editor.HasUnsavedChanges,
            "無変更の画面内更新は既存の親/子IDと更新版、未変更判定を維持");
        list.SelectedIndex = 0; await Wait(() => editor.SelectedDetailKey == group.Details[0].Key);
        Find<TextBox>(root, "ReceiptItemQuantity").Text = "0"; await InvokeAsync(() => Find<Button>(root, "ReceiptDetailApply"));
        Check(group.Details[0].Values.Quantity == 2 && Find<TextBox>(root, "ReceiptItemQuantity").Text == "0", "検証失敗でも保存済み明細と不正数量を保持");
        Find<TextBox>(root, "ReceiptItemQuantity").Text = "2";
        Check(!editor.NeedsLeaveConfirmation, "検証失敗後も値を元に戻すと未変更へ戻る");
        list.SelectedIndex = 1; await Wait(() => editor.SelectedDetailKey == group.Details[1].Key);
        Check(editor.Draft!.Tax == ReceiptTaxSelection.Exempt && !Find<ContentDialog>(root, "ReceiptConfirmationDialog").IsLoaded, "変更のない別行選択は確認なしで非課税を復元");
        Check(writes == 0, "既存編集も全体保存APIを呼ばない");
    }
}
