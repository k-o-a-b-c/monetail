using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Receipts;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed partial class ReceiptEditorStateTests
{
    [Theory]
    [InlineData(ReceiptDraftChoice.Stay)] [InlineData(ReceiptDraftChoice.DiscardAndMove)] [InlineData(ReceiptDraftChoice.ApplyAndMove)]
    public void ThreeChoicesApplyToOriginalGroupOrDiscardOnlyFormOrStay(ReceiptDraftChoice choice)
    {
        var state = ReceiptEditorState.Edit(Saved(), Defaults); var first = state.Groups[0]; var second = state.Groups[1];
        state.SelectGroup(first.Key); state.SetGroupMemo("別の未保存変更"); state.SelectDetail(first.Details[0].Key); state.Draft!.Name = "フォームの変更";
        var draft = state.Draft; Assert.False(state.SelectGroup(second.Key)); Assert.Equal(first.Key, state.SelectedGroupKey);
        state.ResolveTransition(choice); Assert.Null(state.PendingTransition); Assert.Equal("別の未保存変更", first.Memo);
        Assert.Equal(choice == ReceiptDraftChoice.ApplyAndMove ? "フォームの変更" : "明細A", first.Details[0].Values.Name);
        Assert.Equal(choice == ReceiptDraftChoice.Stay ? first.Key : second.Key, state.SelectedGroupKey);
        if (choice == ReceiptDraftChoice.Stay) Assert.Same(draft, state.Draft);
    }
    [Fact]
    public void SameSelectionNeverReinitializesDirtyFormAndForeignActionsCannotBypassPendingChoice()
    {
        var state = ReceiptEditorState.Edit(Saved(), Defaults); var group = state.Groups[0]; state.SelectGroup(group.Key); state.SelectDetail(group.Details[0].Key);
        var draft = state.Draft!; draft.Name = "未反映"; Assert.True(state.SelectDetail(group.Details[0].Key)); Assert.True(state.SelectGroup(group.Key)); Assert.Same(draft, state.Draft);
        Assert.Null(state.PendingTransition); Assert.False(state.SelectDetail(group.Details[1].Key));
        Assert.Throws<InvalidOperationException>(() => state.DeleteSelectedDetail()); Assert.Throws<InvalidOperationException>(() => state.ApplyDraft()); Assert.Throws<InvalidOperationException>(() => state.PrepareSave(ReceiptSaveKind.Update));
        Assert.Same(draft, state.Draft); state.ResolveTransition(ReceiptDraftChoice.Stay);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SwitchingDetailCommitsToOriginalRowOrPreservesItWhenDiscarded(bool apply)
    {
        var state = ReceiptEditorState.Edit(Saved(), Defaults); var group = state.Groups[0]; state.SelectGroup(group.Key); state.SelectDetail(group.Details[0].Key);
        state.Draft!.UnitPrice = 123; Assert.False(state.SelectDetail(group.Details[1].Key)); state.ResolveTransition(apply ? ReceiptDraftChoice.ApplyAndMove : ReceiptDraftChoice.DiscardAndMove);
        Assert.Equal(apply ? 123 : 100, group.Details[0].Values.UnitPrice); Assert.Equal(group.Details[1].Key, state.SelectedDetailKey); Assert.Equal(-20, state.Draft!.UnitPrice); Assert.Empty(state.Draft.PersonIds);
    }
    [Fact]
    public void FailedApplyAndMoveRetainsAllDataAndClearsOnlyThePendingDialogIntent()
    {
        var state = ReceiptEditorState.Edit(Saved(), Defaults); var group = state.Groups[0]; state.SelectGroup(group.Key); state.SelectDetail(group.Details[0].Key);
        var draft = state.Draft!; draft.Quantity = 0; Assert.False(state.SelectGroup(state.Groups[1].Key));
        Assert.Throws<ReceiptInputException>(() => state.ResolveTransition(ReceiptDraftChoice.ApplyAndMove));
        Assert.Null(state.PendingTransition); Assert.Equal(group.Key, state.SelectedGroupKey); Assert.Equal(group.Details[0].Key, state.SelectedDetailKey); Assert.Same(draft, state.Draft);
        Assert.Equal(0, draft.Quantity); Assert.Equal(2, group.Details[0].Values.Quantity); draft.Quantity = 3; state.ApplyDraft(); Assert.Equal(3, group.Details[0].Values.Quantity);
    }
    [Theory]
    [InlineData(ReceiptDraftChoice.Stay)] [InlineData(ReceiptDraftChoice.DiscardAndMove)]
    public void InvalidInputCanStillBeCancelledOrExplicitlyDiscarded(ReceiptDraftChoice choice)
    {
        var state = ReceiptEditorState.Edit(Saved(), Defaults); state.SelectGroup(state.Groups[0].Key); state.SelectDetail(state.Groups[0].Details[0].Key); var key = state.SelectedGroupKey;
        state.Draft!.UnitPrice = null; state.Draft.Tax = ReceiptTaxSelection.Unselected; state.SelectGroup(state.Groups[1].Key); state.ResolveTransition(choice);
        Assert.Equal(choice == ReceiptDraftChoice.Stay ? key : state.Groups[1].Key, state.SelectedGroupKey); Assert.Equal(100, state.Groups[0].Details[0].Values.UnitPrice);
    }
    [Fact]
    public void NewDetailBeforeGroupSwitchIsAddedToSourceGroupAndPeopleCarryToDestination()
    {
        var state = New(); state.AddCategory(Category); Fill(state); state.Draft!.PersonIds.Add(Person);
        Assert.False(state.AddCategory(OtherCategory)); Assert.Single(state.Groups); state.ResolveTransition(ReceiptDraftChoice.ApplyAndMove);
        Assert.Equal(2, state.Groups.Count); Assert.Single(state.Groups[0].Details); Assert.Empty(state.Groups[1].Details); Assert.Equal(state.Groups[1].Key, state.SelectedGroupKey);
        Assert.Equal(new[] { Person }, state.Draft!.PersonIds); Assert.False(state.HasUnappliedChanges);
    }
    [Fact]
    public void GroupDeleteWaitsForDraftResolutionAndValidationFailureDoesNotRemoveAnything()
    {
        var state = ReceiptEditorState.Edit(Saved(), Defaults); state.SelectGroup(state.Groups[0].Key); state.SelectDetail(state.Groups[0].Details[0].Key); state.Draft!.Quantity = 0;
        Assert.False(state.RemoveSelectedGroup(true)); Assert.Equal(2, state.Groups.Count); Assert.Throws<ReceiptInputException>(() => state.ResolveTransition(ReceiptDraftChoice.ApplyAndMove)); Assert.Equal(2, state.Groups.Count);
        Assert.False(state.RemoveSelectedGroup(true)); state.ResolveTransition(ReceiptDraftChoice.DiscardAndMove); Assert.Single(state.Groups); Assert.Null(state.Draft);
    }
    [Fact]
    public void DefaultsOrInjectedValidationFailureHappensBeforeAnyMutation()
    {
        var fail = false; var state = ReceiptEditorState.Create(Header(), (id, at) => fail && id == OtherCategory ? throw new ReceiptInputException("taxRate", "初期値失敗") : Defaults(id, at));
        state.AddCategory(Category); Fill(state); state.AddCategory(OtherCategory); fail = true;
        Assert.Throws<ReceiptInputException>(() => state.ResolveTransition(ReceiptDraftChoice.ApplyAndMove)); Assert.Single(state.Groups); Assert.Empty(state.Groups[0].Details); Assert.Equal("追加", state.Draft!.Name);
        var validation = ReceiptEditorState.Create(Header(), Defaults, _ => throw new ReceiptInputException("amount", "検証失敗")); validation.AddCategory(Category); Fill(validation);
        Assert.Throws<ReceiptInputException>(() => validation.ApplyDraft()); Assert.Empty(validation.Groups[0].Details); Assert.Equal(100, validation.Draft!.UnitPrice);
    }
}
