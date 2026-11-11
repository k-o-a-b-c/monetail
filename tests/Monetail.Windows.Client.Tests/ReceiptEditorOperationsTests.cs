using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Receipts;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed partial class ReceiptEditorStateTests
{
    [Fact]
    public void GroupsAreUniqueAndSwitchingKeepsRowsMemoAndInheritedPeople()
    {
        var state = New(); state.AddCategory(Category); var first = state.Groups[0]; Fill(state); state.Draft!.PersonIds.Add(Person); state.ApplyDraft(); state.SetGroupMemo("編集メモ");
        Assert.Throws<ReceiptInputException>(() => state.AddCategory(Category)); Assert.Single(state.Groups); Assert.Equal(first.Key, state.SelectedGroupKey);
        state.AddCategory(OtherCategory); var second = state.Groups[1]; Assert.Equal("Excluded", state.Draft!.ExpenseType); Assert.Equal(new[] { Person }, state.Draft.PersonIds);
        Fill(state, "収入", 50); state.ApplyDraft(); state.SelectGroup(first.Key);
        Assert.Single(first.Details); Assert.Single(second.Details); Assert.Equal("編集メモ", first.Memo); Assert.Equal("Variable", state.Draft!.ExpenseType);
        var snapshot = state.PrepareSave(ReceiptSaveKind.Register).Snapshot; Assert.Equal(2, snapshot.Groups.Count); Assert.Equal("編集メモ", snapshot.Groups[0].Memo);
    }
    [Fact]
    public void UpdatePreservesIdsAndReuseOnlyDetachesTheFormUntilAdd()
    {
        var state = ReceiptEditorState.Edit(Saved(), Defaults); var group = state.Groups[0]; var row = group.Details[0];
        state.SelectGroup(group.Key); state.SelectDetail(row.Key); state.Draft!.Name = "更新名"; state.ApplyDraft();
        Assert.Same(row, group.Details[0]); Assert.NotNull(row.DetailId); Assert.Equal("更新名", row.Values.Name); Assert.Equal(new[] { Person }, state.Draft!.PersonIds);
        state.SelectDetail(row.Key); state.Draft!.Memo = "未反映の流用値"; var draft = state.Draft; state.ReuseSelectedDetail();
        Assert.Same(draft, state.Draft); Assert.Null(state.SelectedDetailKey); Assert.Equal(2, group.Details.Count); Assert.Equal("Aメモ", row.Values.Memo);
        Assert.Equal(ReceiptTaxSelection.Exempt, state.Draft!.Tax); Assert.True(state.HasUnappliedChanges);
        state.ApplyDraft(); var added = group.Details[^1]; Assert.Null(added.DetailId); Assert.NotEqual(row.Key, added.Key); Assert.Equal("未反映の流用値", added.Values.Memo);
        Assert.Equal(ReceiptTaxSelection.Exempt, added.Values.Tax); Assert.Equal(new[] { Person }, added.Values.PersonIds);
    }
    [Fact]
    public void DeleteClearsSelectionAndDoesNotDeleteAnotherRowOnRepeat()
    {
        var state = ReceiptEditorState.Edit(Saved(), Defaults); var group = state.Groups[0]; state.SelectGroup(group.Key); state.SelectDetail(group.Details[0].Key);
        state.Draft!.Name = "削除とともに破棄"; state.Draft.Quantity = -1;
        Assert.True(state.DeleteSelectedDetail()); Assert.Single(group.Details); Assert.Null(state.SelectedDetailKey); Assert.False(state.HasUnappliedChanges);
        Assert.False(state.DeleteSelectedDetail()); Assert.Single(group.Details); Assert.Equal("", state.Draft!.Name); Assert.Null(state.Draft.UnitPrice);
    }
    [Fact]
    public void MoveKeepsDirtyFormSelectionAndReturningToOriginalSparseOrderClearsOrderChanges()
    {
        var state = ReceiptEditorState.Edit(Saved(), Defaults); var group = state.Groups[0]; var row = group.Details[0];
        state.SelectGroup(group.Key); state.SelectDetail(row.Key); var draft = state.Draft!; draft.Name = "途中";
        Assert.False(state.MoveSelectedDetail(-1)); Assert.True(state.MoveSelectedDetail(1)); Assert.Same(draft, state.Draft); Assert.Equal(row.Key, state.SelectedDetailKey);
        Assert.Equal("途中", state.Draft!.Name); Assert.True(state.HasUnappliedChanges); Assert.True(state.HasUnsavedChanges);
        Assert.True(state.MoveSelectedDetail(-1)); Assert.False(state.HasUnsavedChanges); Assert.True(state.HasUnappliedChanges);
        draft.Name = "明細A"; Assert.False(state.NeedsLeaveConfirmation);
        Assert.Equal(new[] { 4, 9 }, state.PrepareSave(ReceiptSaveKind.Update).Snapshot.Groups[0].Details.Select(d => d.DisplayOrder));
    }
    [Fact]
    public void CannotSelectOrEditADetailFromAnUnselectedGroup()
    {
        var state = ReceiptEditorState.Edit(Saved(), Defaults); state.SelectGroup(state.Groups[0].Key); var draft = state.Draft;
        Assert.Throws<ReceiptInputException>(() => state.SelectDetail(state.Groups[1].Details[0].Key)); Assert.Same(draft, state.Draft); Assert.Null(state.SelectedDetailKey);
        Assert.Throws<ArgumentOutOfRangeException>(() => state.MoveSelectedDetail(0));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void GroupRemovalCanBeCancelledOrConfirmedWithoutAutoSelectingAnotherGroup(bool confirmed)
    {
        var state = ReceiptEditorState.Edit(Saved(), Defaults); state.SelectGroup(state.Groups[0].Key); var key = state.SelectedGroupKey; var draft = state.Draft;
        Assert.Equal(confirmed, state.RemoveSelectedGroup(confirmed)); Assert.Equal(confirmed ? 1 : 2, state.Groups.Count);
        if (confirmed) { Assert.Null(state.SelectedGroupKey); Assert.Null(state.SelectedDetailKey); Assert.Null(state.Draft); }
        else { Assert.Equal(key, state.SelectedGroupKey); Assert.Same(draft, state.Draft); Assert.False(state.NeedsLeaveConfirmation); }
    }
    [Fact]
    public void ReaddingAnUncommittedRemovedCategoryKeepsItsExistingServerGroupId()
    {
        var source = Saved(); var state = ReceiptEditorState.Edit(source, Defaults); state.SelectGroup(state.Groups[0].Key); state.RemoveSelectedGroup(true); state.AddCategory(Category);
        Assert.Equal(source.Groups[0].GroupId, state.SelectedGroup!.GroupId); Assert.Empty(state.SelectedGroup.Details);
        Fill(state); state.ApplyDraft(); var group = state.PrepareSave(ReceiptSaveKind.Update).Snapshot.Groups.Single(g => g.CategoryId == Category);
        Assert.Equal(source.Groups[0].GroupId, group.GroupId); Assert.Null(Assert.Single(group.Details).DetailId);
    }
    [Fact]
    public void AddAndRemoveNewGroupAndRestoreHeaderAndMemoValuesReturnToCleanState()
    {
        var state = New(); var name = state.Header.ShopName; state.Header.ShopName = "変更"; Assert.True(state.HasUnsavedChanges); state.Header.ShopName = name;
        state.AddCategory(Category); state.SetGroupMemo("メモ"); state.RemoveSelectedGroup(true); Assert.False(state.NeedsLeaveConfirmation);
        var edit = ReceiptEditorState.Edit(Saved(), Defaults); edit.SelectGroup(edit.Groups[0].Key); edit.SetGroupMemo("変更"); Assert.True(edit.HasUnsavedChanges);
        edit.SetGroupMemo("科目メモ"); Assert.False(edit.HasUnsavedChanges);
    }
    [Theory]
    [InlineData("income")] [InlineData("expense")]
    public void NewDetailDefaultsAreCleanAndPeopleAreInheritedAfterAddAndUpdate(string kind)
    {
        var state = New(); state.AddCategory(kind == "income" ? OtherCategory : Category); var draft = state.Draft!;
        Assert.Equal(kind == "income" ? "Excluded" : "Variable", draft.ExpenseType); Assert.Equal("Irregular", draft.OccurrenceType);
        Assert.Equal(1, draft.Quantity); Assert.Equal(0, draft.UnitDiscountAmount); Assert.Equal(0m, draft.DiscountRate); Assert.Empty(draft.PersonIds); Assert.False(state.HasUnappliedChanges);
        Fill(state); draft.PersonIds.Add(Person); state.ApplyDraft(); Assert.Equal(new[] { Person }, state.Draft!.PersonIds); Assert.False(state.HasUnappliedChanges);
        var row = state.SelectedGroup!.Details[0]; state.SelectDetail(row.Key); state.Draft!.PersonIds.Clear(); state.ApplyDraft(); Assert.Empty(state.Draft!.PersonIds);
    }
    [Theory]
    [InlineData("unselected")] [InlineData("exempt")] [InlineData("zero")]
    public void TaxHasThreeStatesAndOrdinaryInputDoesNotInheritPreviousActualRate(string mode)
    {
        var tax = mode switch { "exempt" => ReceiptTaxSelection.Exempt, "zero" => ReceiptTaxSelection.Taxed(0), _ => ReceiptTaxSelection.Unselected };
        var state = ReceiptEditorState.Create(Header(), (_, _) => new("Expense", tax)); state.AddCategory(Category); Assert.Equal(tax, state.Draft!.Tax); Assert.False(state.HasUnappliedChanges);
        Fill(state); if (mode == "unselected") { Assert.Throws<ReceiptInputException>(() => state.ApplyDraft()); Assert.Empty(state.SelectedGroup!.Details); return; }
        state.Draft!.Tax = ReceiptTaxSelection.Taxed(15m); state.ApplyDraft(); Assert.Equal(tax, state.Draft!.Tax);
        Assert.Equal(ReceiptTaxSelection.Taxed(15m), state.SelectedGroup!.Details[0].Values.Tax);
        Fill(state, "次"); state.ApplyDraft(); var output = state.PrepareSave(ReceiptSaveKind.Register).Snapshot.Groups[0].Details;
        Assert.Equal(mode == "exempt" ? null : (decimal?)0, output[1].TaxRate); Assert.Equal(15m, output[0].TaxRate);
    }
    [Fact]
    public void ExplicitDefaultsRefreshOnlyChangesPristineNewFormAndDoesNotOverwriteManualOrStoredTax()
    {
        var tax = ReceiptTaxSelection.Taxed(10); var state = ReceiptEditorState.Create(Header(), (_, _) => new("Expense", tax)); state.AddCategory(Category);
        tax = ReceiptTaxSelection.Exempt; Assert.True(state.RefreshNewDraftDefaults()); Assert.Equal(tax, state.Draft!.Tax); Assert.False(state.HasUnappliedChanges);
        state.Draft.Tax = ReceiptTaxSelection.Taxed(8); Assert.False(state.RefreshNewDraftDefaults()); Assert.Equal(ReceiptTaxSelection.Taxed(8), state.Draft.Tax);
        Fill(state); state.ApplyDraft(); state.SelectDetail(state.Groups[0].Details[0].Key); Assert.False(state.RefreshNewDraftDefaults()); Assert.Equal(ReceiptTaxSelection.Taxed(8), state.Draft!.Tax);
    }
}
