using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Client.Ledgers;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed class ReceiptDetailInputTests
{
    [Fact]
    public void InitialAndRestoredValuesAreNotChanges()
    {
        var input = new ReceiptDetailInput(new() { ExpenseType = "Variable" });
        Assert.False(input.HasChanges); input.UnitPrice = "途中"; Assert.True(input.HasChanges);
        input.UnitPrice = ""; Assert.False(input.HasChanges);
        input.Quantity = "2"; Assert.True(input.HasChanges); input.Quantity = "1"; Assert.False(input.HasChanges);
    }
    [Theory]
    [InlineData("100+20*3", 160)] [InlineData("-10*2+5", -15)]
    [InlineData("2147483647+2*-1", 2147483645)] [InlineData("-2147483648", int.MinValue)]
    [InlineData("1--2", 3)] [InlineData(" - 2 * 3 + 10 ", 4)]
    public void MoneyExpressionsKeepExistingPrecedence(string text, int expected)
    {
        var input = new ReceiptDetailInput(new()) { UnitPrice = text, UnitDiscountAmount = text };
        Assert.Equal(expected, input.Read().UnitPrice); Assert.Equal(expected, input.Read().UnitDiscountAmount);
    }
    [Theory]
    [InlineData("1+")] [InlineData("2147483647+1-1")] [InlineData("50000*50000")]
    [InlineData("２*３")] [InlineData("1,000")] [InlineData("1 2")] [InlineData("(1+2)*3")]
    public void InvalidMoneyNeverOverwritesDraft(string text)
    {
        var target = new ReceiptDetailDraft { Name = "保存前", UnitPrice = 25 };
        var input = new ReceiptDetailInput(target) { UnitPrice = text }; input.Values.Name = "変更";
        Assert.Throws<ReceiptInputException>(() => input.ApplyTo(target));
        Assert.Equal(25, target.UnitPrice); Assert.Equal("保存前", target.Name); Assert.Equal(text, input.UnitPrice); Assert.True(input.HasChanges);
    }
    [Theory]
    [InlineData("0.1234567890123456789012345678901")]
    [InlineData("1e1")] [InlineData("不正")]
    public void RatesAreNotRoundedOrCorrected(string text)
    {
        var input = new ReceiptDetailInput(new()) { DiscountRate = text };
        Assert.Throws<ReceiptInputException>(() => input.Read()); Assert.Equal(text, input.DiscountRate);
    }
    [Fact]
    public void SemanticRevertAndCopiesKeepPeopleAndTaxStates()
    {
        var draft = new ReceiptDetailDraft { UnitPrice = 100, Tax = ReceiptTaxSelection.Taxed(0) }; draft.PersonIds.Add(Guid.NewGuid());
        var input = new ReceiptDetailInput(draft) { UnitPrice = "50*2" };
        Assert.False(input.HasChanges); input.Values.Tax = ReceiptTaxSelection.Exempt; Assert.True(input.HasChanges);
        input.Values.Tax = ReceiptTaxSelection.Unselected; Assert.NotEqual(input.Read().Tax, draft.Tax);
        input.Values.PersonIds.Clear(); Assert.Single(draft.PersonIds);
    }
}

public sealed partial class ReceiptEditorStateTests
{
    [Theory]
    [InlineData("Fixed")] [InlineData("Variable")] [InlineData("Excluded")]
    public void ExpenseCategoryAllowsAllThreeExpenseTypes(string expense)
    {
        var state = New(); state.AddCategory(Category); state.Draft!.UnitPrice = 1; state.Draft.ExpenseType = expense;
        state.ApplyDraft(); Assert.Equal(expense, state.Groups.Single().Details.Single().Values.ExpenseType);
    }
    [Theory]
    [InlineData("Fixed")] [InlineData("Variable")]
    public void IncomeStillRejectsNonExcludedWithoutMutatingRows(string expense)
    {
        var state = New(); state.AddCategory(OtherCategory); state.Draft!.UnitPrice = 1; state.Draft.ExpenseType = expense;
        Assert.Throws<ReceiptInputException>(() => state.ApplyDraft()); Assert.Empty(state.Groups.Single().Details);
        Assert.Equal(expense, state.Draft.ExpenseType);
    }
}
