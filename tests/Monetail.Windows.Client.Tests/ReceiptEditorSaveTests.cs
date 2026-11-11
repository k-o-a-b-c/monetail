using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed partial class ReceiptEditorStateTests
{
    [Fact]
    public void BothSaveWaysUseCurrentValuesButOnlyUpdateKeepsServerIdentity()
    {
        var source = Saved(); var state = ReceiptEditorState.Edit(source, Defaults);
        state.Header.ShopName = "変更店"; state.Header.Memo = "親メモ";
        state.SelectGroup(state.Groups[0].Key); state.SetGroupMemo("変更科目メモ"); state.SelectDetail(state.Groups[0].Details[0].Key);
        state.Draft!.Name = "変更明細"; state.ApplyDraft();
        var update = state.PrepareSave(ReceiptSaveKind.Update); var reuse = state.PrepareSave(ReceiptSaveKind.ReuseRegistration);
        foreach (var prepared in new[] { update, reuse })
        {
            Assert.Equal("変更店", prepared.Snapshot.ShopName); Assert.Equal("親メモ", prepared.Snapshot.Memo);
            Assert.Equal("変更科目メモ", prepared.Snapshot.Groups[0].Memo); Assert.Equal("変更明細", prepared.Snapshot.Groups[0].Details[0].Name);
        }
        Assert.Equal(source.Revision, update.Revision); Assert.Null(reuse.Revision);
        Assert.All(reuse.Snapshot.Groups, g => { Assert.Null(g.GroupId); Assert.All(g.Details, d => Assert.Null(d.DetailId)); });
        Assert.Equal("店舗", source.ShopName); Assert.Equal("明細A", source.Groups[0].Details[0].Name);
        Assert.True(state.HasUnsavedChanges); Assert.Equal(source.ReceiptId, state.ReceiptId);
    }

    [Theory]
    [InlineData(ReceiptSaveKind.Update)]
    [InlineData(ReceiptSaveKind.ReuseRegistration)]
    public void UnappliedFormBlocksSaveWithoutAutomaticApplyOrDiscard(ReceiptSaveKind kind)
    {
        var state = ReceiptEditorState.Edit(Saved(), Defaults); state.SelectGroup(state.Groups[0].Key); Fill(state);
        var draft = state.Draft; var count = state.Groups[0].Details.Count;
        Assert.Equal("detailDraft", Assert.Throws<ReceiptInputException>(() => state.PrepareSave(kind)).Field);
        Assert.Same(draft, state.Draft); Assert.Equal(count, state.Groups[0].Details.Count); Assert.True(state.HasUnappliedChanges);
        state.Draft!.Name = ""; state.Draft.UnitPrice = null; Assert.False(state.NeedsLeaveConfirmation);
    }

    [Fact]
    public void EmptyGroupsAndIncompleteParentAreEditableButCannotBePrepared()
    {
        var state = New(); state.AddCategory(Category);
        Assert.Throws<ReceiptInputException>(() => state.PrepareSave(ReceiptSaveKind.Register)); Assert.Single(state.Groups);
        Fill(state); state.ApplyDraft(); state.Header.TransactionAt = null;
        Assert.Equal("transactionAt", Assert.Throws<ReceiptInputException>(() => state.PrepareSave(ReceiptSaveKind.Register)).Field);
        Assert.Null(state.Header.TransactionAt); Assert.Single(state.Groups[0].Details);
        state.Header.TransactionAt = At; Assert.NotNull(state.PrepareSave(ReceiptSaveKind.Register));
    }

    [Theory]
    [InlineData(ReceiptSaveKind.Update)]
    [InlineData(ReceiptSaveKind.ReuseRegistration)]
    public void NewEditorRejectsExistingOnlySaveWays(ReceiptSaveKind kind) =>
        Assert.Throws<ReceiptInputException>(() => New().PrepareSave(kind));

    [Fact]
    public void ExistingEditorRejectsRegisterAndPendingTransitionBlocksPreparation()
    {
        var state = ReceiptEditorState.Edit(Saved(), Defaults);
        Assert.Throws<ReceiptInputException>(() => state.PrepareSave(ReceiptSaveKind.Register));
        state.SelectGroup(state.Groups[0].Key); Fill(state); state.SelectGroup(state.Groups[1].Key);
        Assert.Throws<InvalidOperationException>(() => state.PrepareSave(ReceiptSaveKind.Update)); Assert.NotNull(state.PendingTransition);
    }

    [Fact]
    public void InputSourceExportAndVisibleValuesCannotMutateStoredRowsOrBaseline()
    {
        var source = Saved(); var person = source.Groups[0].Details[0].PersonIds[0]; var state = ReceiptEditorState.Edit(source, Defaults);
        source.Groups[0].Details[0].PersonIds[0] = Guid.NewGuid();
        var visible = state.Groups[0].Details[0].Values; visible.Name = "表示コピー"; visible.PersonIds.Clear();
        var prepared = state.PrepareSave(ReceiptSaveKind.Update);
        var people = Assert.IsType<Guid[]>(prepared.Snapshot.Groups[0].Details[0].PersonIds); people[0] = Guid.NewGuid();
        var again = state.PrepareSave(ReceiptSaveKind.Update);
        Assert.Equal(person, Assert.Single(again.Snapshot.Groups[0].Details[0].PersonIds));
        Assert.Equal("明細A", again.Snapshot.Groups[0].Details[0].Name); Assert.False(state.NeedsLeaveConfirmation);
        state.SelectGroup(state.Groups[0].Key); state.SelectDetail(state.Groups[0].Details[0].Key);
        state.Draft!.Memo = "変更"; Assert.True(state.HasUnappliedChanges); state.Draft.Memo = "Aメモ"; Assert.False(state.HasUnappliedChanges);
    }

    [Fact]
    public void PersonSetOrderAndPresentationChangesAreCleanButActualPeopleChangeIsDirty()
    {
        var source = Saved(); var second = Guid.NewGuid(); source.Groups[0].Details[0] = source.Groups[0].Details[0] with { PersonIds = [Person, second] };
        var state = ReceiptEditorState.Edit(source, Defaults); state.SelectGroup(state.Groups[0].Key); state.SelectDetail(state.Groups[0].Details[0].Key);
        state.Draft!.PersonIds.Reverse(); Assert.False(state.NeedsLeaveConfirmation);
        state.Draft.PersonIds.Remove(second); Assert.True(state.HasUnappliedChanges); Assert.False(state.HasUnsavedChanges);
        state.Draft.PersonIds.Add(second); Assert.False(state.NeedsLeaveConfirmation);
    }

    [Fact]
    public void SnapshotValidationFailureKeepsOriginalStateAndCanBeRetriedAfterCorrection()
    {
        var fail = true; var state = ReceiptEditorState.Edit(Saved(), Defaults, validateSnapshot: _ => {
            if (fail) throw new ReceiptInputException("paymentMethodId", "有効期間を確認してください。");
        });
        Assert.Throws<ReceiptInputException>(() => state.PrepareSave(ReceiptSaveKind.ReuseRegistration)); Assert.False(state.NeedsLeaveConfirmation);
        fail = false; Assert.NotNull(state.PrepareSave(ReceiptSaveKind.ReuseRegistration)); Assert.False(state.NeedsLeaveConfirmation);
    }
}
