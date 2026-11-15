using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transfers;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed partial class TransferEditorStateTests
{
    private static object Capture(TransferEditorState s) => new { s.TransferId, s.Revision, s.ReadOnlyReason, s.Latest, s.HasUnsavedChanges,
        s.Draft.TransactionDate, s.Draft.TransactionTime, s.Draft.Name, s.Draft.Amount, s.Draft.Memo, s.Draft.From, s.Draft.To,
        s.Draft.UnresolvedDateText, s.Draft.UnresolvedTimeText, s.Draft.UnresolvedAmountText };

    [Theory]
    [InlineData("date")] [InlineData("time")] [InlineData("name")] [InlineData("from")] [InlineData("to")] [InlineData("amount")] [InlineData("memo")]
    public void TransferEditorReturningEachValueToBaselineClearsDirtyState(string field)
    {
        var saved = Saved(); var state = TransferEditorState.Edit(saved);
        void Change(bool revert) {
            switch (field) {
                case "date": state.Draft.SetDate(DateOnly.FromDateTime(saved.TransactionAt).AddDays(revert ? 0 : 1)); break;
                case "time": state.Draft.SetTime(revert ? TimeOnly.FromDateTime(saved.TransactionAt) : TimeOnly.MinValue); break;
                case "name": state.Draft.Name = revert ? saved.Name : "変更"; break;
                case "from": state.SelectFrom(revert ? From : Other); break; case "to": state.SelectTo(revert ? To : Other); break;
                case "amount": state.Draft.SetAmount(revert ? saved.Amount : 1); break; default: state.Draft.Memo = revert ? "" : " "; break;
            }
        }
        Change(false); Assert.True(state.NeedsLeaveConfirmation); Change(true); Assert.False(state.HasUnsavedChanges);
        state.SelectFrom(From); state.SelectTo(To); Assert.Equal(TransferSelectionSource.Manual, state.Draft.From.Source); Assert.False(state.HasUnsavedChanges);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData(" ")]
    public void TransferEditorMemoComparesEmptyAsNullButPreservesWhitespaceAndSource(string? memo)
    {
        var saved = Saved() with { Memo = memo }; var state = TransferEditorState.Edit(saved);
        Assert.Equal(memo, state.Draft.Memo); Assert.False(state.HasUnsavedChanges);
        state.Draft.Memo = ""; Assert.Equal(memo == " ", state.HasUnsavedChanges);
        state.Draft.Memo = null; Assert.Equal(memo == " ", state.HasUnsavedChanges);
        state.Draft.Memo = memo; Assert.False(state.HasUnsavedChanges);
        Assert.Equal(memo is "" ? null : memo, state.PrepareSave(TransferSaveKind.Update).Snapshot.Memo); Assert.Equal(memo, state.Draft.Memo);
    }

    [Theory]
    [InlineData("date", "")] [InlineData("date", "2026/1")]
    [InlineData("time", "")] [InlineData("time", "12:")]
    [InlineData("amount", "")] [InlineData("amount", "1a")] [InlineData("amount", "2147483648")]
    public void TransferEditorRawIncompleteInputIsRetainedAndBlocksOldParsedValues(string field, string text)
    {
        var saved = Saved(); var state = TransferEditorState.Edit(saved);
        if (field == "date") state.Draft.SetDate(state.Draft.TransactionDate, text);
        else if (field == "time") state.Draft.SetTime(state.Draft.TransactionTime, text);
        else state.Draft.SetAmount(saved.Amount, text);
        var before = Capture(state); var error = Assert.Throws<TransferInputException>(() => state.PrepareSave(TransferSaveKind.Update));
        Assert.Equal(field == "amount" ? "amount" : "transactionAt", error.Field); Assert.Equal(before, Capture(state)); Assert.True(state.HasUnsavedChanges);
        if (field == "date") { Assert.Equal(text, state.Draft.UnresolvedDateText); Assert.Null(state.HasSavedDateChanged); state.Draft.SetDate(DateOnly.FromDateTime(saved.TransactionAt)); }
        else if (field == "time") { Assert.Equal(text, state.Draft.UnresolvedTimeText); Assert.Null(state.IsMidnight); state.Draft.SetTime(TimeOnly.FromDateTime(saved.TransactionAt)); }
        else { Assert.Equal(text, state.Draft.UnresolvedAmountText); state.Draft.SetAmount(saved.Amount); }
        Assert.False(state.HasUnsavedChanges); Assert.Equal(saved.Revision, state.PrepareSave(TransferSaveKind.Update).Revision);
    }

    [Theory]
    [InlineData("date", "transactionAt")] [InlineData("time", "transactionAt")] [InlineData("fraction", "transactionAt")]
    [InlineData("name", "name")] [InlineData("long-name", "name")] [InlineData("from", "fromPaymentMethodId")]
    [InlineData("to", "toPaymentMethodId")] [InlineData("same", "toPaymentMethodId")] [InlineData("amount", "amount")]
    [InlineData("zero", "amount")] [InlineData("negative", "amount")] [InlineData("long-memo", "memo")]
    [InlineData("cr", "memo")] [InlineData("lf", "memo")] [InlineData("vt", "memo")] [InlineData("ff", "memo")]
    [InlineData("nel", "memo")] [InlineData("ls", "memo")] [InlineData("ps", "memo")]
    public void TransferEditorPreparationReusesWriteValidationAndNeverRepairsInvalidInputs(string invalid, string field)
    {
        var state = TransferEditorState.Edit(Saved());
        switch (invalid) {
            case "date": state.Draft.SetDate(null); break; case "time": state.Draft.SetTime(null); break;
            case "fraction": state.Draft.SetTime(new TimeOnly(12, 0, 0, 1)); break; case "name": state.Draft.Name = "\u3000\t "; break;
            case "long-name": state.Draft.Name = string.Concat(Enumerable.Repeat("😀", 51)); break;
            case "from": state.SelectFrom(null); break; case "to": state.SelectTo(null); break; case "same": state.SelectTo(From); break;
            case "amount": state.Draft.SetAmount(null); break; case "zero": state.Draft.SetAmount(0); break; case "negative": state.Draft.SetAmount(int.MinValue); break;
            case "long-memo": state.Draft.Memo = new string('m', 101); break;
            default: state.Draft.Memo = "a" + (invalid switch { "cr" => "\r", "lf" => "\n", "vt" => "\v", "ff" => "\f", "nel" => "\u0085", "ls" => "\u2028", _ => "\u2029" }) + "b"; break;
        }
        var before = Capture(state); var error = Assert.Throws<TransferInputException>(() => state.PrepareSave(TransferSaveKind.Update));
        Assert.Equal(field, error.Field); Assert.Equal(before, Capture(state));
        Assert.Equal(new(state.TransferId!.Value, state.Revision!.Value), state.PrepareDelete()); Assert.Equal(before, Capture(state));
    }

    [Theory]
    [InlineData(false, TransferSaveKind.Register, true)] [InlineData(false, TransferSaveKind.Update, false)] [InlineData(false, TransferSaveKind.ReuseRegistration, false)]
    [InlineData(true, TransferSaveKind.Register, false)] [InlineData(true, TransferSaveKind.Update, true)] [InlineData(true, TransferSaveKind.ReuseRegistration, true)]
    [InlineData(false, (TransferSaveKind)99, false)] [InlineData(true, (TransferSaveKind)99, false)]
    public void TransferEditorSaveModeMatchesNewOrExistingWithoutChangingState(bool existing, TransferSaveKind kind, bool allowed)
    {
        var state = existing ? TransferEditorState.Edit(Saved()) : New(); if (!existing) Fill(state); var before = Capture(state);
        if (allowed) Assert.Equal(kind, state.PrepareSave(kind).Kind);
        else Assert.Equal("saveKind", Assert.Throws<TransferInputException>(() => state.PrepareSave(kind)).Field);
        Assert.Equal(before, Capture(state));
    }

    [Fact]
    public void TransferEditorWarningsUseCurrentValidValuesAndStoredBaseline()
    {
        var state = TransferEditorState.Edit(Saved()); Assert.False(state.IsMidnight); Assert.False(state.HasSavedDateChanged);
        state.Draft.SetTime(TimeOnly.MinValue); Assert.True(state.IsMidnight); Assert.False(state.HasSavedDateChanged);
        state.Draft.SetDate(Day); Assert.True(state.HasSavedDateChanged); state.Draft.SetDate(Day, "2026/"); Assert.Null(state.IsMidnight); Assert.Null(state.HasSavedDateChanged);
        state.Draft.SetDate(Day); Assert.True(state.IsMidnight); Assert.True(state.HasSavedDateChanged);
        var saved = state.PrepareSave(TransferSaveKind.ReuseRegistration); Assert.Equal(Day.ToDateTime(TimeOnly.MinValue), saved.Snapshot.TransactionAt);
        Assert.Throws<TransferInputException>(() => New().PrepareDelete());
    }
}
