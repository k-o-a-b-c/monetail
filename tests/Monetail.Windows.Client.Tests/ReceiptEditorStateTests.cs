using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed partial class ReceiptEditorStateTests
{
    private static readonly Guid Category = Guid.NewGuid(), OtherCategory = Guid.NewGuid(), Person = Guid.NewGuid();
    private static readonly DateTime At = new(2026, 11, 10, 12, 34, 56);
    private static ReceiptHeaderDraft Header() => new() { TransactionAt = At, ShopName = "店舗", PaymentMethodId = Guid.NewGuid(), TaxInclusionMode = 1, TaxRoundingMode = "half_up" };
    private static ReceiptCategoryDefaults Defaults(Guid category, DateTime? at) => new(category == OtherCategory ? "Income" : "Expense", ReceiptTaxSelection.Taxed(category == OtherCategory ? 8m : 10m));
    private static ReceiptEditorState New() => ReceiptEditorState.Create(Header(), Defaults);
    private static ReceiptInfo Saved()
    {
        var header = Header();
        return new(Guid.NewGuid(), At, header.ShopName, null, header.PaymentMethodId!.Value, 1, "half_up", null, null, null, Guid.NewGuid(), [
            new(Guid.NewGuid(), Category, "科目メモ", [new(Guid.NewGuid(), 4, "明細A", 100, 2, 3, 12.5m, null, "Variable", "Irregular", "Aメモ", [Person]),
                new(Guid.NewGuid(), 9, "明細B", -20, 1, -1, 0m, 0m, "Fixed", "Temporary", null, [])]),
            new(Guid.NewGuid(), OtherCategory, null, [new(Guid.NewGuid(), 0, "収入", 50, 1, 0, 0m, 8m, "Excluded", "Irregular", null, [])])
        ]);
    }
    private static void Fill(ReceiptEditorState state, string name = "追加", int price = 100) { state.Draft!.Name = name; state.Draft.UnitPrice = price; }

    [Fact]
    public void NewStateAppliesDetailWithOnlyScreenKeysAndPreparesRegistrationWithoutRequestId()
    {
        var state = New(); Assert.False(state.NeedsLeaveConfirmation); Assert.Null(state.ReceiptId); Assert.Null(state.Revision);
        Assert.True(state.AddCategory(Category)); var group = Assert.Single(state.Groups);
        Assert.Null(group.GroupId); Assert.NotEqual(Guid.Empty, group.Key); Assert.False(state.HasUnappliedChanges);
        Assert.Null(state.Draft!.UnitPrice); Assert.Equal(1, state.Draft.Quantity); Assert.Empty(state.Draft.PersonIds);
        Fill(state); state.ApplyDraft(); var row = Assert.Single(group.Details); Assert.Null(row.DetailId); Assert.Null(state.SelectedDetailKey);
        var prepared = state.PrepareSave(ReceiptSaveKind.Register); Assert.Null(prepared.ReceiptId); Assert.Null(prepared.Revision);
        Assert.Null(prepared.Snapshot.Groups[0].GroupId); Assert.Null(prepared.Snapshot.Groups[0].Details[0].DetailId);
        Assert.True(state.HasUnsavedChanges); Assert.False(state.HasUnappliedChanges); Assert.Equal(group.Key, state.SelectedGroupKey);
    }

    [Fact]
    public void OpeningEditIsCleanAndUnchangedReuseStripsAllSavedIdsWithoutChangingSourceOrEdit()
    {
        var source = Saved(); var state = ReceiptEditorState.Edit(source, Defaults); Assert.False(state.NeedsLeaveConfirmation); Assert.Null(state.SelectedGroupKey);
        var update = state.PrepareSave(ReceiptSaveKind.Update); var reuse = state.PrepareSave(ReceiptSaveKind.ReuseRegistration);
        Assert.Equal(source.ReceiptId, update.ReceiptId); Assert.Equal(source.Revision, update.Revision); Assert.Null(reuse.ReceiptId); Assert.Null(reuse.Revision);
        Assert.Equal(source.Groups[0].GroupId, update.Snapshot.Groups[0].GroupId); Assert.Equal(source.Groups[0].Details[0].DetailId, update.Snapshot.Groups[0].Details[0].DetailId);
        Assert.All(reuse.Snapshot.Groups, g => { Assert.Null(g.GroupId); Assert.All(g.Details, d => Assert.Null(d.DetailId)); });
        Assert.Equal(source.Groups[0].Details[0].PersonIds, reuse.Snapshot.Groups[0].Details[0].PersonIds);
        Assert.Null(reuse.Snapshot.Groups[0].Details[0].TaxRate); Assert.Equal(0m, reuse.Snapshot.Groups[0].Details[1].TaxRate);
        Assert.Equal(new[] { 4, 9 }, update.Snapshot.Groups[0].Details.Select(d => d.DisplayOrder)); Assert.False(state.NeedsLeaveConfirmation);
        Assert.Equal(source.ReceiptId, state.ReceiptId); Assert.Equal(source.Revision, state.Revision);
    }

    [Fact]
    public void DirtySelectionWaitsForChoiceAndCancelKeepsOriginalDraftSelectionAndData()
    {
        var state = ReceiptEditorState.Edit(Saved(), Defaults); state.SelectGroup(state.Groups[0].Key); state.SelectDetail(state.Groups[0].Details[0].Key);
        var selected = state.SelectedDetailKey; var group = state.SelectedGroupKey; var draft = state.Draft!; draft.Name = "未反映";
        Assert.False(state.SelectGroup(state.Groups[1].Key)); Assert.NotNull(state.PendingTransition); Assert.Equal(group, state.SelectedGroupKey); Assert.Equal(selected, state.SelectedDetailKey); Assert.Same(draft, state.Draft);
        state.ResolveTransition(ReceiptDraftChoice.Stay); Assert.Null(state.PendingTransition); Assert.Same(draft, state.Draft); Assert.Equal("明細A", state.Groups[0].Details[0].Values.Name);
        Assert.True(state.HasUnappliedChanges); Assert.False(state.HasUnsavedChanges);
    }
}
