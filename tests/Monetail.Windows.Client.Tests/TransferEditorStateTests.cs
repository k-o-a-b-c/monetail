using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transfers;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed partial class TransferEditorStateTests
{
    private static readonly Guid From = Guid.CreateVersion7(), To = Guid.CreateVersion7(), Other = Guid.CreateVersion7();
    private static readonly DateOnly Day = new(2026, 11, 15);
    private static readonly DateTime At = new(2026, 10, 31, 12, 34, 56, DateTimeKind.Unspecified);
    private static TransferInfo Saved() => new(Guid.CreateVersion7(), At, " 銀行から引出 ", From, To, 12345, null, Guid.NewGuid());
    private static TransferEditorState New() => TransferEditorState.Create(Day);
    private static void Fill(TransferEditorState state)
    {
        state.Draft.Name = " 銀行から引出 "; state.SelectFrom(From); state.SelectTo(To); state.Draft.SetAmount(12345);
    }

    [Fact]
    public void TransferEditorNewHasCallerDateMidnightEmptyFieldsAndNoSavedIdentity()
    {
        var state = New(); Assert.False(state.IsExisting); Assert.Null(state.TransferId); Assert.Null(state.Revision);
        Assert.Equal(Day.ToDateTime(TimeOnly.MinValue), state.Draft.TransactionAt); Assert.Equal(DateTimeKind.Unspecified, state.Draft.TransactionAt!.Value.Kind);
        Assert.Equal("", state.Draft.Name); Assert.Null(state.Draft.Amount); Assert.Null(state.Draft.Memo);
        Assert.Equal(TransferSelectionSource.Unselected, state.Draft.From.Source); Assert.Null(state.Draft.From.PaymentMethodId); Assert.Null(state.Draft.To.PaymentMethodId);
        Assert.False(state.HasUnsavedChanges); Assert.False(state.NeedsLeaveConfirmation); Assert.True(state.IsMidnight); Assert.False(state.HasSavedDateChanged);
        Fill(state); var prepared = state.PrepareSave(TransferSaveKind.Register);
        Assert.Null(prepared.TransferId); Assert.Null(prepared.Revision); Assert.Equal(state.Draft.Name, prepared.Snapshot.Name); Assert.Equal(12345, prepared.Snapshot.Amount);
        Assert.True(state.HasUnsavedChanges); Assert.Null(state.TransferId); Assert.Null(state.Revision);
    }

    [Fact]
    public void TransferEditorExistingAndReusePreserveValuesUntilExplicitlyChanged()
    {
        var saved = Saved(); var original = saved with { }; var state = TransferEditorState.Edit(saved);
        Assert.True(state.IsExisting); Assert.False(state.HasUnsavedChanges); Assert.Equal(saved.TransferId, state.TransferId); Assert.Equal(saved.Revision, state.Revision);
        Assert.Equal(TransferSelectionSource.Saved, state.Draft.From.Source); Assert.Equal(TransferSelectionSource.Saved, state.Draft.To.Source);
        var updated = state.PrepareSave(TransferSaveKind.Update); var reused = state.PrepareSave(TransferSaveKind.ReuseRegistration);
        Assert.Equal(saved.TransferId, updated.TransferId); Assert.Equal(saved.Revision, updated.Revision); Assert.Null(reused.TransferId); Assert.Null(reused.Revision);
        Assert.Equal(updated.Snapshot, reused.Snapshot); Assert.NotSame(updated.Snapshot, reused.Snapshot); Assert.Equal(saved.TransactionAt, reused.Snapshot.TransactionAt);
        state.Draft.Name = "編集した名前"; state.Draft.SetAmount(1); var changed = state.PrepareSave(TransferSaveKind.ReuseRegistration);
        Assert.Equal("編集した名前", changed.Snapshot.Name); Assert.Equal(1, changed.Snapshot.Amount); Assert.Equal(saved.Revision, state.Revision); Assert.Equal(12345, updated.Snapshot.Amount);
        Assert.Equal(saved.Name, updated.Snapshot.Name); Assert.Equal(original, saved);
    }

    [Fact]
    public void TransferEditorIncompleteNewSaveDoesNotDirtyOrCompleteMissingValues()
    {
        var state = New(); var before = Capture(state);
        Assert.Throws<TransferInputException>(() => state.PrepareSave(TransferSaveKind.Register)); Assert.Equal(before, Capture(state)); Assert.False(state.HasUnsavedChanges);
        state.Draft.Name = "入力"; state.SelectFrom(From); state.Draft.SetAmount(1); before = Capture(state);
        Assert.Equal("toPaymentMethodId", Assert.Throws<TransferInputException>(() => state.PrepareSave(TransferSaveKind.Register)).Field);
        Assert.Equal(before, Capture(state)); Assert.Null(state.Draft.To.PaymentMethodId);
    }

    [Theory]
    [InlineData("前\r後")]
    [InlineData("前\n後")]
    [InlineData("前\r\n後")]
    public void TransferEditorRejectsNewlineMemoWithoutChangingInputOrSavedIdentity(string memo)
    {
        var saved = Saved(); var state = TransferEditorState.Edit(saved); state.Draft.Memo = memo;
        var before = Capture(state);
        foreach (var kind in new[] { TransferSaveKind.Update, TransferSaveKind.ReuseRegistration })
        {
            Assert.Equal("memo", Assert.Throws<TransferInputException>(() => state.PrepareSave(kind)).Field);
            Assert.Equal(memo, state.Draft.Memo); Assert.Equal(before, Capture(state));
            Assert.Equal(saved.TransferId, state.TransferId); Assert.Equal(saved.Revision, state.Revision);
        }
        Assert.True(state.HasUnsavedChanges); state.Draft.Memo = null; Assert.False(state.HasUnsavedChanges);
    }

    [Fact]
    public void TransferEditorRejectsIdenticalPaymentMethodsAndKeepsBothSelections()
    {
        var state = TransferEditorState.Edit(Saved()); state.SelectTo(From); var before = Capture(state);
        Assert.Equal("toPaymentMethodId", Assert.Throws<TransferInputException>(() => state.PrepareSave(TransferSaveKind.Update)).Field);
        Assert.Equal(before, Capture(state)); Assert.Equal(From, state.Draft.From.PaymentMethodId); Assert.Equal(From, state.Draft.To.PaymentMethodId);
        state.SelectTo(To); Assert.False(state.HasUnsavedChanges); Assert.Equal(To, state.PrepareSave(TransferSaveKind.Update).Snapshot.ToPaymentMethodId);
    }

    [Fact]
    public void TransferEditorSavedPaymentIdsRemainThePermissionBaselineUntilLatestIsAdopted()
    {
        var fresh = New(); Fill(fresh);
        Assert.Null(fresh.SavedFromPaymentMethodId); Assert.Null(fresh.SavedToPaymentMethodId);
        var saved = Saved(); var state = TransferEditorState.Edit(saved);
        state.SelectFrom(To); state.SelectTo(Other);
        Assert.Equal(From, state.SavedFromPaymentMethodId); Assert.Equal(To, state.SavedToPaymentMethodId);
        var latest = saved with { FromPaymentMethodId = Other, ToPaymentMethodId = From, Revision = Guid.NewGuid() };
        state.RegisterConflict(latest); state.AdoptLatest(false);
        Assert.Equal(From, state.SavedFromPaymentMethodId); Assert.Equal(To, state.SavedToPaymentMethodId);
        state.AdoptLatest(true);
        Assert.Equal(Other, state.SavedFromPaymentMethodId); Assert.Equal(From, state.SavedToPaymentMethodId);
    }
}
