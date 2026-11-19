using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Presentation.Receipts;
using Xunit;

namespace Monetail.Windows.Presentation.Tests;

public sealed class ReceiptEditorDetailsViewModelTests
{
    private static readonly Guid Category = Guid.NewGuid(), OtherCategory = Guid.NewGuid(), Person = Guid.NewGuid();
    private static readonly DateTime At = new(2026, 11, 19, 12, 0, 0);
    private static readonly CategoriesInfo Categories = new([
        new(Category, "食費", "Expense", "Standard", null, null, 0, Guid.NewGuid()),
        new(OtherCategory, "給料", "Income", "Exempt", null, null, 1, Guid.NewGuid())], Guid.NewGuid(), "Admin");
    private static ReceiptEditorViewModel Create()
    {
        var editor = ReceiptEditorState.Create(new()
        {
            TransactionAt = At,
            TaxInclusionMode = 1,
            TaxRoundingMode = "floor"
        },
            (category, _) => new(category == Category ? "Expense" : "Income", ReceiptTaxSelection.Exempt));
        var model = new ReceiptEditorViewModel(editor, () => new(false, "", false),
            () => (new PeopleInfo([new(Person, "本人", null, null, 0, Guid.NewGuid())], Person, "Admin", Guid.NewGuid()), null, Categories, Person))
        {
            TaxHistory = new([])
        };
        model.RebuildGroups();
        return model;
    }
    private static async Task Add(ReceiptEditorViewModel model, Guid? category = null) =>
        await model.TransitionCommand.ExecuteAsync(new ReceiptEditTransition(ReceiptTransitionKind.AddGroup, CategoryId: category ?? Category));
    private static void Fill(ReceiptEditorViewModel model, string name = "明細", string price = "100")
    {
        var input = Assert.IsType<ReceiptDetailInput>(model.DetailInput);
        input.Values.Name = name;
        input.UnitPrice = price;
        model.SynchronizeDetail();
    }
    [Fact]
    public async Task SelectionAndProjectionUseOnlyEditorKeysAndExistingInput()
    {
        var model = Create();
        await Add(model);
        var group = Assert.Single(model.Editor.Groups);
        Assert.Equal(group.Key, model.SelectedGroup!.Key);
        Assert.Null(model.SelectedDetail);
        Assert.True(model.CanApply);
        Assert.False(model.CanAddCategory(Category));
        Assert.Equal("食費", model.SelectedGroup.Name);
        Assert.Equal("食費", model.CategoryChoices(null, false)[0].ToString());
        Fill(model);
        var draft = model.Editor.Draft;
        var source = model.DetailInput;
        Assert.Same(source, model.DetailInput);
        model.Calculate(new(2026, 11, 19), true);
        Assert.Same(draft, model.Editor.Draft);
        Assert.True(model.ApplyDetail());
        Assert.Null(model.Editor.SelectedDetailKey);
        Assert.Equal(100, Assert.Single(model.Editor.SelectedGroup!.Details).Values.UnitPrice);
        Assert.Null(model.Editor.SelectedGroup.Details[0].DetailId);
    }
    [Theory]
    [InlineData(ReceiptDraftChoice.Stay)]
    [InlineData(ReceiptDraftChoice.DiscardAndMove)]
    [InlineData(ReceiptDraftChoice.ApplyAndMove)]
    public async Task ThreeChoicesPreserveOrApplyOnlyTheOriginalDraft(ReceiptDraftChoice choice)
    {
        var model = Create();
        await Add(model);
        var first = model.Editor.SelectedGroup!;
        Fill(model, "保持する下書き");
        var input = model.DetailInput;
        model.SetGroupMemo("残す科目メモ");
        model.ConfirmationRequested += _ => Task.FromResult(choice);
        await Add(model, OtherCategory);
        Assert.Equal("残す科目メモ", first.Memo);
        Assert.Null(model.Editor.PendingTransition);
        if (choice == ReceiptDraftChoice.Stay)
        {
            Assert.Single(model.Editor.Groups);
            Assert.Same(input, model.DetailInput);
            Assert.Equal(first.Key, model.Editor.SelectedGroupKey);
            Assert.Equal("保持する下書き", input!.Values.Name);
        }
        else
        {
            Assert.Equal(2, model.Editor.Groups.Count);
            Assert.Equal(OtherCategory, model.Editor.SelectedGroup!.CategoryId);
            Assert.NotSame(input, model.DetailInput);
        }
        Assert.Equal(choice == ReceiptDraftChoice.ApplyAndMove ? 1 : 0, first.Details.Count);
    }
    [Fact]
    public async Task InvalidRawInputCannotMoveAndNoConfirmationResultDestroysIt()
    {
        var model = Create();
        await Add(model);
        Fill(model, price: "123x");
        var input = model.DetailInput;
        var key = model.Editor.SelectedGroupKey;
        model.ConfirmationRequested += _ => Task.FromResult(ReceiptDraftChoice.ApplyAndMove);
        await Add(model, OtherCategory);
        Assert.Same(input, model.DetailInput);
        Assert.Equal("123x", input!.UnitPrice);
        Assert.Equal(key, model.Editor.SelectedGroupKey);
        Assert.Single(model.Editor.Groups);
        Assert.Empty(model.Editor.SelectedGroup!.Details);
        Assert.NotEmpty(model.Calculate(new(2026, 11, 19), true).InputError);
    }
    [Fact]
    public async Task TransitionRunningIsDerivedFromTheCommandAndDoesNotStoreAnotherTarget()
    {
        var model = Create();
        await Add(model);
        Fill(model);
        var gate = new TaskCompletionSource<ReceiptDraftChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        model.ConfirmationRequested += _ => gate.Task;
        var key = model.Editor.SelectedGroupKey;
        var input = model.DetailInput;
        var changes = new List<string?>();
        model.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        var running = model.TransitionCommand.ExecuteAsync(new ReceiptEditTransition(ReceiptTransitionKind.AddGroup, CategoryId: OtherCategory));
        Assert.True(model.IsTransitioning);
        Assert.False(model.TransitionCommand.CanExecute(new ReceiptEditTransition(ReceiptTransitionKind.AddGroup, CategoryId: OtherCategory)));
        Assert.Same(input, model.DetailInput);
        Assert.Equal(key, model.Editor.SelectedGroupKey);
        Assert.Null(model.Editor.PendingTransition);
        Assert.False(model.ApplyDetail());
        gate.SetResult(ReceiptDraftChoice.Stay);
        await running;
        Assert.False(model.IsTransitioning);
        Assert.Same(input, model.DetailInput);
        Assert.Contains(nameof(model.IsTransitioning), changes);
    }
    [Fact]
    public async Task DetailSelectionReusesRowsAndUnchangedReselectionNeedsNoPrompt()
    {
        var model = Create();
        await Add(model);
        Fill(model);
        Assert.True(model.ApplyDetail());
        var row = model.Details[0];
        var prompts = 0;
        model.ConfirmationRequested += _ =>
        {
            prompts++;
            return Task.FromResult(ReceiptDraftChoice.Stay);
        };
        var observedInput = false;
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(model.DetailInput))
            {
                Assert.Equal(row.Key, model.SelectedDetail?.Key);
                Assert.Equal("明細", model.DetailInput?.Values.Name);
                observedInput = true;
            }
        };
        await model.TransitionCommand.ExecuteAsync(new ReceiptEditTransition(ReceiptTransitionKind.SelectDetail, DetailKey: row.Key));
        Assert.True(observedInput);
        Assert.Same(row, model.SelectedDetail);
        Assert.Same(row, model.Details[0]);
        Assert.Equal(0, prompts);
        Assert.Equal("更新", model.ApplyText);
        var input = model.DetailInput;
        await model.TransitionCommand.ExecuteAsync(new ReceiptEditTransition(ReceiptTransitionKind.SelectDetail, DetailKey: row.Key));
        Assert.Same(input, model.DetailInput);
        Assert.Equal(0, prompts);
    }
    [Fact]
    public async Task ReuseAndMoveKeepRawInputWhileDeleteClearsSelectionAndCannotDeleteAnotherRow()
    {
        var model = Create();
        await Add(model);
        Fill(model, "先頭");
        Assert.True(model.ApplyDetail());
        Fill(model, "次");
        Assert.True(model.ApplyDetail());
        var first = model.Details[0].Key;
        await model.TransitionCommand.ExecuteAsync(new ReceiptEditTransition(ReceiptTransitionKind.SelectDetail, DetailKey: first));
        var input = model.DetailInput!;
        input.UnitPrice = "不正文字列";
        model.MoveDetail(1);
        Assert.Same(input, model.DetailInput);
        Assert.Equal(first, model.Editor.SelectedDetailKey);
        Assert.Equal(first, model.Details[1].Key);
        model.ReuseDetail();
        Assert.Same(input, model.DetailInput);
        Assert.Equal("不正文字列", input.UnitPrice);
        Assert.Null(model.Editor.SelectedDetailKey);
        Assert.Equal(2, model.Details.Length);
        model.ConfirmationRequested += _ => Task.FromResult(ReceiptDraftChoice.DiscardAndMove);
        await model.TransitionCommand.ExecuteAsync(new ReceiptEditTransition(ReceiptTransitionKind.SelectDetail, DetailKey: first));
        Assert.True(model.DeleteDetail());
        Assert.Null(model.Editor.SelectedDetailKey);
        Assert.False(model.DeleteDetail());
        Assert.Single(model.Editor.SelectedGroup!.Details);
    }
    [Fact]
    public async Task CategoryDeleteResolvesDraftBeforeTheSeparateDeleteConfirmation()
    {
        var model = Create();
        await Add(model);
        Fill(model);
        var prompts = new List<ReceiptEditorConfirmation>();
        model.ConfirmationRequested += kind =>
        {
            prompts.Add(kind);
            return Task.FromResult(kind == ReceiptEditorConfirmation.UnappliedDetail ? ReceiptDraftChoice.ApplyAndMove : ReceiptDraftChoice.Stay);
        };
        await model.TransitionCommand.ExecuteAsync(new ReceiptEditTransition(ReceiptTransitionKind.RemoveGroup));
        Assert.Equal(new[] { ReceiptEditorConfirmation.UnappliedDetail, ReceiptEditorConfirmation.RemoveCategory }, prompts);
        Assert.Single(model.Editor.Groups);
        Assert.Single(model.Editor.SelectedGroup!.Details);
        Assert.Null(model.Editor.SelectedDetailKey);
    }
    [Fact]
    public async Task PeopleAndTaxWarningsComeFromExistingInputAndNeverChangeIt()
    {
        var model = Create();
        await Add(model);
        var input = model.DetailInput!;
        var missing = Guid.NewGuid();
        input.Values.PersonIds.Add(missing);
        input.Values.Tax = ReceiptTaxSelection.Taxed(0m);
        input.UnitPrice = "bad";
        var result = model.Calculate(new(2026, 11, 19), true);
        Assert.Contains("再選択", result.TaxWarning);
        Assert.NotEmpty(result.InputError);
        Assert.Equal(new[] { Person, missing }, model.PeopleChoices().Select(p => p.Id));
        Assert.Equal("bad", input.UnitPrice);
        Assert.Equal(0m, input.Values.Tax.Rate);
        Assert.Contains(model.TaxChoices(new(2026, 11, 19)), c => c.Tax == ReceiptTaxSelection.Taxed(0m));
        Assert.Contains(model.TaxChoices(new(2026, 11, 19)), c => c.Tax == ReceiptTaxSelection.Exempt);
    }
    [Fact]
    public async Task DiscardReleasesDetailAndProjectionReferencesWithoutMutatingTheOriginalRows()
    {
        var model = Create();
        await Add(model);
        Fill(model);
        Assert.True(model.ApplyDetail());
        var group = model.Editor.SelectedGroup;
        model.Discard();
        model.Discard();
        Assert.Null(model.DetailInput);
        Assert.Null(model.TaxHistory);
        Assert.Empty(model.Groups);
        Assert.Empty(model.Details);
        Assert.Single(group!.Details);
    }
}
