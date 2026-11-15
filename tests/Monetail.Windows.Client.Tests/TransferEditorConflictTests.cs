using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transfers;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed partial class TransferEditorStateTests
{
    [Fact]
    public void TransferEditorConflictKeepsOldInputAndRevisionUntilConfirmedAdoption()
    {
        var saved = Saved(); var state = TransferEditorState.Edit(saved);
        state.Draft.Name = "未保存"; state.Draft.SetDate(null, "2026/"); state.Draft.SetAmount(null, "1a"); state.Draft.Memo = "入力メモ";
        var latest = saved with { TransactionAt = At.AddDays(2), Name = "最新", FromPaymentMethodId = Other, ToPaymentMethodId = From, Amount = 888, Memo = "保存メモ", Revision = Guid.NewGuid() };
        state.RegisterConflict(latest); Assert.Equal("未保存", state.Draft.Name); Assert.Equal("2026/", state.Draft.UnresolvedDateText); Assert.Equal(saved.Revision, state.Revision);
        Assert.Equal(latest, state.Latest); Assert.True(state.IsReadOnly); var before = Capture(state);
        Assert.False(state.AdoptLatest(false)); Assert.Equal(before, Capture(state));
        Assert.True(state.AdoptLatest(true)); Assert.False(state.IsReadOnly); Assert.Null(state.Latest); Assert.False(state.HasUnsavedChanges);
        Assert.Equal(latest.Revision, state.Revision); Assert.Equal(latest.TransferId, state.TransferId); Assert.Equal(latest.TransactionAt, state.Draft.TransactionAt);
        Assert.Equal(latest.Name, state.Draft.Name); Assert.Equal(888, state.Draft.Amount); Assert.Equal(latest.Memo, state.Draft.Memo);
        Assert.Equal(Other, state.Draft.From.PaymentMethodId); Assert.Equal(From, state.Draft.To.PaymentMethodId); Assert.Equal(TransferSelectionSource.Saved, state.Draft.From.Source);
        Assert.False(state.HasSavedDateChanged); Assert.Equal(latest.Revision, state.PrepareSave(TransferSaveKind.Update).Revision);
        Assert.Throws<InvalidOperationException>(() => state.AdoptLatest(true));
    }

    [Theory]
    [InlineData(TransferReadOnlyReason.Conflict)] [InlineData(TransferReadOnlyReason.Forbidden)] [InlineData(TransferReadOnlyReason.NotFound)]
    public void TransferEditorReadOnlyKeepsReadableValuesAndBlocksEveryMutationAndPreparation(TransferReadOnlyReason reason)
    {
        var state = TransferEditorState.Edit(Saved()); state.Draft.Name = "未保存"; state.EnterReadOnly(reason); var before = Capture(state);
        Action[] changes = [() => state.Draft.Name = "変更", () => state.Draft.Memo = "変更", () => state.Draft.SetDate(Day), () => state.Draft.SetTime(TimeOnly.MinValue),
            () => state.Draft.SetAmount(1), () => state.SelectFrom(Other), () => state.SelectTo(Other), () => state.PrepareSave(TransferSaveKind.Update),
            () => state.PrepareSave(TransferSaveKind.ReuseRegistration), () => state.PrepareSave(TransferSaveKind.Register), () => state.PrepareDelete()];
        foreach (var change in changes) { Assert.Throws<InvalidOperationException>(change); Assert.Equal(before, Capture(state)); }
        Assert.Null(state.BeginAutofill()); Assert.Equal("未保存", state.Draft.Name); Assert.True(state.NeedsLeaveConfirmation); Assert.Equal(reason, state.ReadOnlyReason);
    }

    [Theory]
    [InlineData("other-id")] [InlineData("v7-revision")] [InlineData("variant")] [InlineData("same-payments")] [InlineData("invalid-amount")]
    public void TransferEditorInvalidLatestCannotDestroyUnsavedInputOrChangeBaseline(string invalid)
    {
        var saved = Saved(); var state = TransferEditorState.Edit(saved); state.Draft.Name = "入力保持";
        var latest = invalid switch { "other-id" => saved with { TransferId = Guid.CreateVersion7() }, "v7-revision" => saved with { Revision = Guid.CreateVersion7() },
            "variant" => saved with { Revision = Guid.Parse("01234567-89ab-4def-0123-456789abcdef") }, "same-payments" => saved with { ToPaymentMethodId = From }, _ => saved with { Amount = 0 } };
        var before = Capture(state); Assert.Throws<TransferInputException>(() => state.RegisterConflict(latest)); Assert.Equal(before, Capture(state));
    }

    [Theory]
    [InlineData(TransferReadOnlyReason.Forbidden)] [InlineData(TransferReadOnlyReason.NotFound)]
    public void TransferEditorAccessLossDoesNotPermitOldConflictToRestoreWriting(TransferReadOnlyReason reason)
    {
        var saved = Saved(); var state = TransferEditorState.Edit(saved); state.RegisterConflict(saved with { Revision = Guid.NewGuid() }); state.EnterReadOnly(reason); var before = Capture(state);
        Assert.Throws<InvalidOperationException>(() => state.AdoptLatest(true)); Assert.Throws<InvalidOperationException>(() => state.RegisterConflict(saved));
        Assert.Throws<InvalidOperationException>(() => state.EnterReadOnly(TransferReadOnlyReason.Conflict)); Assert.Equal(before, Capture(state));
    }

    [Fact]
    public void TransferEditorReadOnlyInvalidatesPendingAutofillAndInvalidSelectionsAreAtomic()
    {
        var state = AutofillNew(); var before = Capture(state);
        Assert.Throws<TransferInputException>(() => state.SelectFrom(Guid.Empty)); Assert.Throws<TransferInputException>(() => state.SelectTo(Guid.Empty)); Assert.Equal(before, Capture(state));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.EnterReadOnly(TransferReadOnlyReason.None)); Assert.Throws<ArgumentOutOfRangeException>(() => state.EnterReadOnly((TransferReadOnlyReason)99)); Assert.Equal(before, Capture(state));
        var context = state.BeginAutofill()!; state.EnterReadOnly(TransferReadOnlyReason.Forbidden); before = Capture(state);
        Assert.False(state.TryApplyAutofill(context, new(From, To))); Assert.Equal(before, Capture(state));
        Assert.Throws<TransferInputException>(() => New().RegisterConflict(Saved()));
    }
}
