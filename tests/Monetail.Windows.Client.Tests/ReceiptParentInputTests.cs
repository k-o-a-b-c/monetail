using Monetail.Windows.Client.Receipts;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed class ReceiptParentInputTests
{
    private static ReceiptEditorState Editor() => ReceiptEditorState.Create(new() { TaxInclusionMode = 0, TaxRoundingMode = "floor" }, (_, _) => new("Expense", ReceiptTaxSelection.Unselected));
    [Fact]
    public void InitialAndRepeatedExplicitSelectionAreIndependentFromValueChanges()
    {
        var editor = Editor(); var input = new ReceiptParentInputState(editor.Header, false);
        Assert.Equal(ReceiptInputSource.Initial, input.TaxInclusionSource); Assert.Equal(ReceiptInputSource.Initial, input.TaxRoundingSource);
        Assert.True(input.CanAutofillTaxInclusion); Assert.True(input.CanAutofillTaxRounding); Assert.False(editor.HasUnsavedChanges);
        input.SelectTaxInclusion(0); Assert.Equal(ReceiptInputSource.Manual, input.TaxInclusionSource); Assert.False(input.CanAutofillTaxInclusion);
        Assert.True(input.CanAutofillTaxRounding); Assert.False(editor.HasUnsavedChanges);
        input.SelectTaxRounding("floor"); Assert.Equal(ReceiptInputSource.Manual, input.TaxRoundingSource); Assert.False(editor.HasUnsavedChanges);
    }
    [Fact]
    public void ManualValuesAreProtectedEvenAfterReturningToOriginalValues()
    {
        var editor = Editor(); var input = new ReceiptParentInputState(editor.Header, false);
        input.SelectTaxInclusion(1); input.SelectTaxRounding("half_up"); Assert.True(editor.HasUnsavedChanges);
        input.SelectTaxInclusion(0); input.SelectTaxRounding("floor"); Assert.False(editor.HasUnsavedChanges);
        Assert.False(input.TryAutofillTaxInclusion(1)); Assert.False(input.TryAutofillTaxRounding("half_up"));
        Assert.Equal(0, editor.Header.TaxInclusionMode); Assert.Equal("floor", editor.Header.TaxRoundingMode);
    }
    [Fact]
    public void AutomaticFieldsCanChangeBetweenShopsAndMissingHistoryRestoresInitialValues()
    {
        var editor = Editor(); var input = new ReceiptParentInputState(editor.Header, false);
        Assert.True(input.TryAutofillTaxInclusion(1)); Assert.True(input.TryAutofillTaxRounding("half_up"));
        Assert.Equal(ReceiptInputSource.Automatic, input.TaxInclusionSource); Assert.Equal(ReceiptInputSource.Automatic, input.TaxRoundingSource);
        input.SelectTaxInclusion(1); Assert.False(input.TryAutofillTaxInclusion(0)); Assert.True(input.TryAutofillTaxRounding(null));
        Assert.Equal(1, editor.Header.TaxInclusionMode); Assert.Equal("floor", editor.Header.TaxRoundingMode);
        Assert.Equal(ReceiptInputSource.Initial, input.TaxRoundingSource);
    }
    [Fact]
    public void SavedReceiptValuesCannotBeAutofilled()
    {
        var header = new ReceiptHeaderDraft { TaxInclusionMode = 1, TaxRoundingMode = "half_up" }; var input = new ReceiptParentInputState(header, true);
        Assert.Equal(ReceiptInputSource.Saved, input.TaxInclusionSource); Assert.False(input.TryAutofillTaxInclusion(0)); Assert.False(input.TryAutofillTaxRounding(null));
        Assert.Equal(1, header.TaxInclusionMode); Assert.Equal("half_up", header.TaxRoundingMode);
    }
    [Theory]
    [InlineData("", "Month", true, null, null)] [InlineData("", "Year", true, null, null)]
    [InlineData("1", "Week", true, 1, "Week")] [InlineData("2147483647", "Year", true, 2147483647, "Year")]
    [InlineData("0", "Month", false, null, null)] [InlineData("-1", "Month", false, null, null)]
    [InlineData("2147483648", "Month", false, null, null)] [InlineData("1.5", "Month", false, null, null)]
    [InlineData("abc", "Month", false, null, null)] [InlineData(" ", "Month", false, null, null)]
    [InlineData("2", null, false, null, null)] [InlineData("2", "Day", false, null, null)]
    public void RecurrenceSeparatesBlankFromInvalidText(string text, string? unit, bool valid, int? interval, string? savedUnit)
    {
        var result = ReceiptRecurrenceInput.Parse(text, unit);
        Assert.Equal(valid, result.IsValid); Assert.Equal(interval, result.Interval); Assert.Equal(savedUnit, result.Unit);
    }
}
