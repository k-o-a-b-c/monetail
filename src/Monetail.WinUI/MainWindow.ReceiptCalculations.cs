using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Client.Input;
using Monetail.Windows.Client.Receipts;

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
    private DateOnly? ConfirmedReceiptDay => DateOnly.TryParseExact(ReceiptDate.Text, "yyyy/MM/dd", CultureInfo.InvariantCulture,
        DateTimeStyles.None, out var day) ? day : null;
    private async void RetryReceiptOpen(object sender, RoutedEventArgs args) => await OpenReceiptEditorAsync(receiptOpenRetryId);

    private void RefreshReceiptTaxChoices()
    {
        if (receiptTaxRates is null) return;
        var selected = receiptDetailInput?.Values.Tax ?? ReceiptTaxSelection.Unselected;
        var choices = ReceiptTaxRules.Candidates(receiptTaxRates, ConfirmedReceiptDay).ToList();
        // 期限外の現在値も保持して表示する。選択更新イベントで入力へ書き戻さない。
        if (selected.IsSelected && !choices.Contains(selected)) choices.Add(selected);
        var rendering = receiptDetailRendering; receiptDetailRendering = true;
        try
        {
            ReceiptItemTax.Items.Clear();
            foreach (var choice in choices)
            {
                var status = ReceiptTaxRules.Check(receiptTaxRates, ConfirmedReceiptDay, choice);
                var suffix = status == ReceiptTaxValidity.NeedsReselection ? "（要再選択）" : status == ReceiptTaxValidity.Unknown ? "（日付未確定）" : "";
                ReceiptItemTax.Items.Add(new ComboBoxItem { Content = ReceiptDetailRow.TaxLabel(choice) + suffix, Tag = choice });
            }
            ReceiptItemTax.SelectedItem = ReceiptItemTax.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (ReceiptTaxSelection)i.Tag == selected);
        }
        finally { receiptDetailRendering = rendering; }
    }
    private void RefreshReceiptCalculation()
    {
        if (receiptEditor is not { } editor || receiptEditorRendering) return;
        var confirmed = ConfirmedReceiptDay is not null && TimeOnly.TryParseExact(ReceiptTime.Text, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) &&
            ReceiptRecurrenceInput.Parse(ReceiptInterval.Text, ReceiptChoice(ReceiptUnit)).IsValid;
        var result = ReceiptCalculationDisplay.Evaluate(editor, receiptTaxRates, ConfirmedReceiptDay, confirmed);
        foreach (var row in ReceiptCategories.Items.OfType<ReceiptCategoryRow>()) row.SetAmount(result.Groups.TryGetValue(row.Key, out var amount) ? amount : null);
        foreach (var row in ReceiptDetailsList.Items.OfType<ReceiptDetailRow>()) row.SetCalculation(result.Details.TryGetValue(row.Key, out var amount) ? amount : null,
            result.Taxes.GetValueOrDefault(row.Key, ReceiptTaxValidity.Unknown));
        ReceiptAccountingTotal.Text = "会計合計（反映済み明細）：" + (result.Amounts?.TotalAmount.ToString("N0", CultureInfo.CurrentCulture) ?? "未計算");
        ReceiptCalculationNotice.Text = result.Message ?? "";
        ReceiptCalculationNotice.Visibility = result.Message is null ? Visibility.Collapsed : Visibility.Visible;
        var tax = ReceiptTaxRules.Check(receiptTaxRates, ConfirmedReceiptDay, receiptDetailInput?.Values.Tax ?? ReceiptTaxSelection.Unselected);
        var warning = tax switch { ReceiptTaxValidity.Unselected => "税率を選択してください。", ReceiptTaxValidity.NeedsReselection => "この税率は取引日に適用できません。値を保持しています。税率を再選択してください。", ReceiptTaxValidity.Unknown => "取引日が未確定のため、税率を判定できません。", _ => "" };
        if (receiptDetailInput is { } input)
            try { _ = input.Read(); } catch (Monetail.Windows.Client.Ledgers.ReceiptInputException error) { warning = error.Message; }
        ReceiptItemTaxWarning.Text = warning; ReceiptItemTaxWarning.Visibility = receiptDetailInput is not null && warning.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
