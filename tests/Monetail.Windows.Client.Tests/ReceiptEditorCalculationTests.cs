using System.Text.Json;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed partial class ReceiptEditorStateTests
{
    [Fact]
    public void SharedCalculationReadsExistingEditorWithoutChangingIdentitySelectionDraftOrDirtyState()
    {
        var saved = Saved(); var editor = ReceiptEditorState.Edit(saved, Defaults);
        var group = editor.Groups[0]; Assert.True(editor.SelectGroup(group.Key));
        var baseline = JsonSerializer.Serialize(editor.PrepareSave(ReceiptSaveKind.Update));
        var draft = editor.Draft; var key = editor.SelectedGroupKey;
        var actual = ReceiptCalculations.Calculate(editor);
        var expected = ReceiptCalculations.Calculate(editor.PrepareSave(ReceiptSaveKind.Update).Snapshot);
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
        Assert.Equal(baseline, JsonSerializer.Serialize(editor.PrepareSave(ReceiptSaveKind.Update)));
        Assert.Same(draft, editor.Draft); Assert.Equal(key, editor.SelectedGroupKey); Assert.False(editor.NeedsLeaveConfirmation);
        Assert.Equal(saved.ReceiptId, editor.ReceiptId); Assert.Equal(saved.Revision, editor.Revision);
    }
    [Fact]
    public void SharedCalculationRejectsUnappliedInputWithoutApplyingOrDiscardingIt()
    {
        var editor = New(); editor.AddCategory(Category); Fill(editor); editor.ApplyDraft();
        editor.Draft!.Name = "未反映"; editor.Draft.Quantity = 0;
        var draft = editor.Draft; var key = editor.SelectedGroupKey; var rows = JsonSerializer.Serialize(editor.Groups);
        var exception = Assert.Throws<ReceiptInputException>(() => ReceiptCalculations.Calculate(editor));
        Assert.Equal("detailDraft", exception.Field); Assert.Same(draft, editor.Draft); Assert.Equal("未反映", editor.Draft.Name);
        Assert.Equal(key, editor.SelectedGroupKey); Assert.Equal(rows, JsonSerializer.Serialize(editor.Groups)); Assert.True(editor.HasUnappliedChanges);
    }
    [Fact]
    public void SharedCalculationDoesNotRequirePaymentSelectionOrPerformSaveAndDoesNotCorrectEmptyGroups()
    {
        var editor = New(); editor.Header.PaymentMethodId = null; editor.AddCategory(Category); Fill(editor); editor.ApplyDraft();
        Assert.NotNull(ReceiptCalculations.Calculate(editor)); Assert.Null(editor.Header.PaymentMethodId);
        editor.AddCategory(OtherCategory);
        var exception = Assert.Throws<Monetail.Calculations.Receipts.ReceiptValidationException>(() => ReceiptCalculations.Calculate(editor));
        Assert.Equal("groups[1].details", exception.Field); Assert.Equal(2, editor.Groups.Count);
    }
}
