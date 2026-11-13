using System.Text.Json;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed partial class ReceiptEditorStateTests
{
    [Fact]
    public void DraftValidationUsesApplyRulesWithoutChangingStoredValuesSelectionOrDirtyState()
    {
        var saved = Saved(); var editor = ReceiptEditorState.Edit(saved, Defaults);
        editor.SelectGroup(editor.Groups[0].Key); editor.SelectDetail(editor.Groups[0].Details[0].Key);
        var group = editor.SelectedGroupKey; var detail = editor.SelectedDetailKey; var draft = editor.Draft!;
        var before = JsonSerializer.Serialize(ReceiptCalculations.FromApplied(editor)); var amounts = ReceiptCalculations.CalculateApplied(editor);
        draft.Quantity = 0;
        var error = Assert.Throws<ReceiptInputException>(() => editor.ValidateDraft());
        Assert.Equal("detailDraft.quantity", error.Field); Assert.True(editor.HasUnappliedChanges);
        Assert.Same(draft, editor.Draft); Assert.Equal(0, draft.Quantity);
        Assert.Equal(before, JsonSerializer.Serialize(ReceiptCalculations.FromApplied(editor)));
        draft.Quantity = saved.Groups[0].Details[0].Quantity; editor.ValidateDraft();
        Assert.False(editor.HasUnappliedChanges); Assert.False(editor.HasUnsavedChanges);
        Assert.Equal(group, editor.SelectedGroupKey); Assert.Equal(detail, editor.SelectedDetailKey);
        Assert.Equal(saved.ReceiptId, editor.ReceiptId); Assert.Equal(saved.Revision, editor.Revision);
        Assert.Equal(JsonSerializer.Serialize(amounts), JsonSerializer.Serialize(ReceiptCalculations.CalculateApplied(editor)));
    }
    [Fact]
    public void RawNumericErrorAndTaxMismatchRemainIndependentUntilEachIsCorrected()
    {
        var day = new DateOnly(2026, 11, 11); var rates = new TaxRatesInfo([new("Standard", day, null, 1)]);
        var draft = new ReceiptDetailDraft { Name = "明細", UnitPrice = 19, ExpenseType = "Variable", Tax = ReceiptTaxSelection.Taxed(10) };
        var input = new ReceiptDetailInput(draft) { UnitPrice = "不正な生文字列" };
        var error = Assert.Throws<ReceiptInputException>(() => input.Read()); Assert.Equal("detailDraft.unitPrice", error.Field);
        Assert.Equal(ReceiptTaxValidity.NeedsReselection, ReceiptTaxRules.Check(rates, day, input.Values.Tax));
        Assert.Equal("不正な生文字列", input.UnitPrice); Assert.Equal(19, draft.UnitPrice);
        input.UnitPrice = "19"; Assert.Equal(19, input.Read().UnitPrice);
        Assert.Equal(ReceiptTaxValidity.NeedsReselection, ReceiptTaxRules.Check(rates, day, input.Values.Tax));
        input.Values.Tax = ReceiptTaxSelection.Taxed(1);
        Assert.Equal(ReceiptTaxValidity.Valid, ReceiptTaxRules.Check(rates, day, input.Values.Tax));
        Assert.Equal(ReceiptTaxSelection.Taxed(10), draft.Tax);
    }
}
