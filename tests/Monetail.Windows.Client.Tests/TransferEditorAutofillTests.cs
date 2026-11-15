using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transfers;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed partial class TransferEditorStateTests
{
    private static TransferEditorState AutofillNew()
    {
        var state = New(); state.Draft.Name = " Ａ銀行 "; state.Draft.SetAmount(321); state.Draft.Memo = " メモ "; return state;
    }
    [Fact]
    public void TransferEditorAutofillUsesExactScopeReplacesAutomaticAndClearsOnlyAutomaticValues()
    {
        var state = AutofillNew(); var context = state.BeginAutofill()!;
        Assert.Equal(" Ａ銀行 ", context.Name); Assert.Equal(Day, context.TransactionDate); Assert.Null(context.FromPaymentMethodId);
        Assert.True(state.TryApplyAutofill(context, new(From, To))); Assert.Equal(From, state.Draft.From.PaymentMethodId); Assert.Equal(To, state.Draft.To.PaymentMethodId);
        Assert.Equal(TransferSelectionSource.Automatic, state.Draft.From.Source); Assert.Equal(TransferSelectionSource.Automatic, state.Draft.To.Source);
        Assert.True(state.TryApplyAutofill(state.BeginAutofill()!, new(To, Other))); Assert.Equal(To, state.Draft.From.PaymentMethodId); Assert.Equal(Other, state.Draft.To.PaymentMethodId);
        Assert.True(state.TryApplyAutofill(state.BeginAutofill()!, new(null, null))); Assert.Null(state.Draft.From.PaymentMethodId); Assert.Null(state.Draft.To.PaymentMethodId);
        Assert.Equal(TransferSelectionSource.Unselected, state.Draft.From.Source); Assert.Equal(321, state.Draft.Amount); Assert.Equal(" メモ ", state.Draft.Memo);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void TransferEditorAutofillProtectsManualChoicesIndependently(bool manualFrom, bool manualTo)
    {
        var state = AutofillNew(); if (manualFrom) state.SelectFrom(From); if (manualTo) state.SelectTo(Other);
        var context = state.BeginAutofill()!; Assert.Equal(manualFrom ? From : null, context.FromPaymentMethodId);
        Assert.True(state.TryApplyAutofill(context, new(From, To)));
        Assert.Equal(manualFrom ? TransferSelectionSource.Manual : TransferSelectionSource.Automatic, state.Draft.From.Source);
        Assert.Equal(manualTo ? Other : To, state.Draft.To.PaymentMethodId);
        Assert.True(state.TryApplyAutofill(state.BeginAutofill()!, new(null, null)));
        Assert.Equal(manualFrom ? From : null, state.Draft.From.PaymentMethodId); Assert.Equal(manualTo ? Other : null, state.Draft.To.PaymentMethodId);
        Assert.Equal(manualTo ? TransferSelectionSource.Manual : TransferSelectionSource.Unselected, state.Draft.To.Source);
    }

    [Theory]
    [InlineData("name")] [InlineData("name-back")] [InlineData("date")] [InlineData("date-back")]
    [InlineData("unresolved-date")] [InlineData("from")] [InlineData("from-back")]
    public void TransferEditorAutofillRejectsResponsesAfterScopeChangesEvenIfValuesReturn(string changed)
    {
        var state = AutofillNew(); var context = state.BeginAutofill()!;
        switch (changed) {
            case "name": state.Draft.Name = "Ｂ銀行"; break; case "name-back": state.Draft.Name = "Ｂ銀行"; state.Draft.Name = context.Name; break;
            case "date": state.Draft.SetDate(Day.AddDays(1)); break; case "date-back": state.Draft.SetDate(Day.AddDays(1)); state.Draft.SetDate(Day); break;
            case "unresolved-date": state.Draft.SetDate(Day, "2026/"); break; case "from": state.SelectFrom(Other); break;
            default: state.SelectFrom(Other); state.SelectFrom(null); break;
        }
        var before = Capture(state); Assert.False(state.TryApplyAutofill(context, new(From, To))); Assert.Equal(before, Capture(state));
    }

    [Fact]
    public void TransferEditorAutofillManualFromChangeScopesNewRequestAndManualToDuringWaitIsProtected()
    {
        var state = AutofillNew(); var old = state.BeginAutofill()!; state.SelectFrom(From);
        Assert.False(state.TryApplyAutofill(old, new(To, Other))); var current = state.BeginAutofill()!; Assert.Equal(From, current.FromPaymentMethodId);
        state.SelectTo(Other); Assert.True(state.TryApplyAutofill(current, new(From, To))); Assert.Equal(Other, state.Draft.To.PaymentMethodId);
        Assert.Equal(TransferSelectionSource.Manual, state.Draft.To.Source);
    }

    [Fact]
    public void TransferEditorAutofillDoesNotOverwriteSavedOrManuallyChangedExistingSelections()
    {
        var saved = Saved(); var state = TransferEditorState.Edit(saved); var another = AutofillNew(); var context = another.BeginAutofill()!;
        Assert.Null(state.BeginAutofill()); Assert.False(state.TryApplyAutofill(context, new(Other, From))); Assert.False(state.HasUnsavedChanges);
        state.SelectFrom(Other); state.Draft.Name = "別名"; Assert.Null(state.BeginAutofill()); Assert.Equal(Other, state.Draft.From.PaymentMethodId); Assert.Equal(To, state.Draft.To.PaymentMethodId);
        Assert.Equal(TransferSelectionSource.Saved, state.Draft.To.Source); Assert.Equal(saved.Revision, state.Revision);
    }

    [Fact]
    public void TransferEditorAutofillNewerContextAndDifferentEditorCannotUseOldResponse()
    {
        var state = AutofillNew(); var other = AutofillNew(); var first = state.BeginAutofill()!; var second = state.BeginAutofill()!;
        Assert.False(other.TryApplyAutofill(second, new(From, To))); Assert.False(state.TryApplyAutofill(first, new(To, Other)));
        Assert.True(state.TryApplyAutofill(second, new(From, To))); var before = Capture(state);
        Assert.False(state.TryApplyAutofill(second, new(To, Other))); Assert.Equal(before, Capture(state));
    }

    [Fact]
    public void TransferEditorAutofillDoesNotInvalidateOnUnrelatedTimeAmountAndMemoChanges()
    {
        var state = AutofillNew(); var context = state.BeginAutofill()!;
        state.Draft.SetTime(null, "12:"); state.Draft.SetAmount(null, "1a"); state.Draft.Memo = "新メモ";
        Assert.True(state.TryApplyAutofill(context, new(From, To))); Assert.Equal("12:", state.Draft.UnresolvedTimeText); Assert.Equal("1a", state.Draft.UnresolvedAmountText); Assert.Equal("新メモ", state.Draft.Memo);
    }

    [Theory]
    [InlineData("half-null")] [InlineData("empty-from")] [InlineData("empty-to")] [InlineData("same")] [InlineData("wrong-from")]
    public void TransferEditorAutofillInvalidResultsDoNotPartiallyChangeInputsOrConsumeContext(string invalid)
    {
        var state = AutofillNew(); state.SelectFrom(From); var context = state.BeginAutofill()!; var before = Capture(state);
        var result = invalid switch { "half-null" => new TransferAutofillInfo(From, null), "empty-from" => new(Guid.Empty, To), "empty-to" => new(From, Guid.Empty), "same" => new(From, From), _ => new(Other, To) };
        Assert.Throws<TransferInputException>(() => state.TryApplyAutofill(context, result)); Assert.Equal(before, Capture(state));
        Assert.True(state.TryApplyAutofill(context, new(From, To))); Assert.Equal(To, state.Draft.To.PaymentMethodId);
    }

    [Theory]
    [InlineData("name")] [InlineData("long-name")] [InlineData("date")] [InlineData("pending-date")]
    public void TransferEditorAutofillIncompleteScopeIsRetainedWithoutPreparingARequest(string missing)
    {
        var state = AutofillNew();
        if (missing == "name") state.Draft.Name = ""; else if (missing == "long-name") state.Draft.Name = new string('a', 101);
        else state.Draft.SetDate(missing == "date" ? null : Day, missing == "pending-date" ? "2026/" : null);
        var before = Capture(state); Assert.Null(state.BeginAutofill()); Assert.Equal(before, Capture(state));
    }
}
