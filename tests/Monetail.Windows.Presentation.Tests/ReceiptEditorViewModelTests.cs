using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Presentation.Receipts;
using Xunit;

namespace Monetail.Windows.Presentation.Tests;

public sealed class ReceiptEditorViewModelTests
{
    private static readonly Guid Mine = Guid.NewGuid(), Other = Guid.NewGuid(), Payment = Guid.NewGuid(), Outside = Guid.NewGuid();
    private static readonly DateTime At = new(2026, 11, 19);
    private static ReceiptEditorState New() => ReceiptEditorState.Create(new()
    {
        TransactionAt = At,
        ShopLocation = "",
        TaxInclusionMode = 0,
        TaxRoundingMode = "floor"
    }, (_, _) => new("Expense", ReceiptTaxSelection.Unselected));
    private static ReceiptInfo Saved(string? location = null) => new(Guid.NewGuid(), At.AddHours(13), "保存店", location,
        Payment, 1, "half_up", 2, "Month", "メモ", Guid.NewGuid(), []);
    private static ReceiptEditorViewModel Model(ReceiptEditorState? state = null) => new(state ?? New(), () => new(false, "", false));
    private static bool Sync(ReceiptEditorViewModel model, string date = "2026/11/19", string time = "00:00:00",
        string name = "", string location = "", string memo = "", Guid? payment = null, string interval = "", string? unit = "Month") =>
        model.UpdateHeader(date, time, name, location, memo, payment, 0, "floor", interval, unit);

    [Fact]
    public void NewModelUsesTheSameHeaderAndDoesNotDirtyInitialInputOrUiOnlyChoices()
    {
        var editor = New();
        var model = Model(editor);
        Assert.Same(editor, model.Editor);
        Assert.True(model.IsNew);
        Assert.Equal("レシートの登録", model.Title);
        Assert.False(Sync(model));
        Assert.False(editor.NeedsLeaveConfirmation);
        Assert.Null(editor.Header.PaymentMethodId);
        Assert.Null(editor.Header.RecurrenceInterval);
        Assert.Null(editor.Header.RecurrenceUnit);
        Assert.False(model.HasUnresolvedInput("2026/11/19", "00:00:00", "", "Year"));
        Assert.False(Sync(model, unit: "Year"));
        Assert.False(editor.NeedsLeaveConfirmation);
    }
    [Fact]
    public void SavedModelKeepsIdentityValuesAndTaxSourcesAcrossRefresh()
    {
        var source = Saved();
        var editor = ReceiptEditorState.Edit(source, (_, _) => new("Expense", ReceiptTaxSelection.Unselected));
        var model = Model(editor);
        var parent = model.ParentInput;
        model.Refresh();
        Assert.True(model.IsExisting);
        Assert.Equal("レシートの編集", model.Title);
        Assert.Equal(source.ReceiptId, editor.ReceiptId);
        Assert.Equal(source.Revision, editor.Revision);
        Assert.Equal(1, editor.Header.TaxInclusionMode);
        Assert.Equal("half_up", editor.Header.TaxRoundingMode);
        Assert.Equal(ReceiptInputSource.Saved, parent.TaxInclusionSource);
        Assert.Equal(ReceiptInputSource.Saved, parent.TaxRoundingSource);
        Assert.Same(parent, model.ParentInput);
        Assert.False(editor.NeedsLeaveConfirmation);
    }
    [Fact]
    public void ManualTaxSelectionProtectsSameValuesWithoutDirtyingTheHeader()
    {
        var model = Model();
        model.ParentInput.SelectTaxInclusion(0);
        model.ParentInput.SelectTaxRounding("floor");
        Assert.False(model.ParentInput.TryAutofillTaxInclusion(1));
        Assert.False(model.ParentInput.TryAutofillTaxRounding("half_up"));
        model.Refresh();
        Assert.Equal(ReceiptInputSource.Manual, model.ParentInput.TaxInclusionSource);
        Assert.Equal(ReceiptInputSource.Manual, model.ParentInput.TaxRoundingSource);
        Assert.False(model.Editor.NeedsLeaveConfirmation);
    }
    [Fact]
    public void UnfinishedDateAndInvalidRecurrenceDoNotOverwriteLastValidValues()
    {
        var model = Model();
        Sync(model, interval: "2");
        Assert.Equal(2, model.Editor.Header.RecurrenceInterval);
        Sync(model, date: "2026/", time: "未確定", interval: "-1");
        Assert.Equal(At, model.Editor.Header.TransactionAt);
        Assert.Equal(2, model.Editor.Header.RecurrenceInterval);
        Assert.Equal("Month", model.Editor.Header.RecurrenceUnit);
        Assert.True(model.IntervalInvalid);
        Assert.True(model.HasUnresolvedInput("2026/", "未確定", "-1", "Month"));
        Sync(model);
        Assert.False(model.IntervalInvalid);
        Assert.Null(model.Editor.Header.RecurrenceInterval);
        Assert.Null(model.Editor.Header.RecurrenceUnit);
        Assert.False(model.Editor.NeedsLeaveConfirmation);
    }
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void UnchangedLocationPreservesSavedNullAndEmptyDistinction(string? location)
    {
        var source = Saved(location);
        var editor = ReceiptEditorState.Edit(source, (_, _) => new("Expense", ReceiptTaxSelection.Unselected));
        var model = Model(editor);
        model.UpdateHeader("2026/11/19", "13:00:00", "保存店", "", "メモ", Payment, 1, "half_up", "2", "Month");
        Assert.Equal(location, editor.Header.ShopLocation);
        Assert.False(editor.NeedsLeaveConfirmation);
    }
    [Fact]
    public void ValidationNotificationsClearWhenInputIsCorrectedWithoutTouchingDetails()
    {
        var editor = New();
        editor.AddCategory(Guid.NewGuid());
        var draft = editor.Draft;
        var model = Model(editor);
        var changes = new List<string?>();
        model.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        Sync(model, memo: new string('あ', 101), interval: "2147483648");
        Assert.True(model.MemoInvalid);
        Assert.True(model.IntervalInvalid);
        Sync(model, memo: " ");
        Assert.False(model.MemoInvalid);
        Assert.False(model.IntervalInvalid);
        Assert.Equal(" ", editor.Header.Memo);
        Assert.Same(draft, editor.Draft);
        Assert.Empty(editor.SelectedGroup!.Details);
        Assert.Contains(nameof(model.MemoInvalid), changes);
        Assert.Contains(nameof(model.IntervalInvalid), changes);
    }
    [Fact]
    public void PaymentChoicesRetainOutOfPeriodAndMissingSelectionsWithoutRewritingInput()
    {
        var model = Model();
        Sync(model, payment: Outside);
        var people = new PeopleInfo([new(Other, "他人", null, null, 0, Guid.NewGuid()),
            new(Mine, "本人", null, null, 1, Guid.NewGuid())], Mine, "Admin", Guid.NewGuid());
        var methods = new PaymentMethodsInfo([new(Payment, Mine, "現金", 1, null, null, Guid.NewGuid()),
            new(Outside, Other, "終了", 0, null, new(2026, 11, 18), Guid.NewGuid())], [], Mine, "Admin");
        var choices = model.PaymentChoices(people, methods, Mine, id => id == Payment ? "現金" : "（他人）終了");
        Assert.Equal(new Guid?[] { Payment, Outside }, choices.Select(c => c.Id));
        Assert.True(model.PaymentInvalid);
        Assert.Equal(Outside, model.Editor.Header.PaymentMethodId);
        var missing = model.PaymentChoices(people, null, Mine, _ => throw new InvalidOperationException());
        Assert.Equal(Outside, Assert.Single(missing).Id);
        Assert.Equal("支払方法名未取得", missing[0].Name);
        Sync(model, payment: Payment);
        model.PaymentChoices(people, methods, Mine, _ => "現金");
        Assert.False(model.PaymentInvalid);
        Sync(model);
        Assert.Equal(Payment, model.Editor.Header.PaymentMethodId);
    }
    [Fact]
    public async Task AuthenticationContextAndCommandsKeepTheSameInputWithoutAutomaticOperations()
    {
        var context = new ReceiptEditorAvailability(false, "", false);
        var editor = New();
        var model = new ReceiptEditorViewModel(editor, () => context);
        var parent = model.ParentInput;
        var actions = new List<ReceiptEditorAction>();
        var changes = new List<string?>();
        model.ActionRequested += action =>
        {
            actions.Add(action);
            return Task.CompletedTask;
        };
        model.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        Sync(model, name: "入力保持");
        context = new(true, "マスタ情報を取得できませんでした", true);
        model.Refresh();
        Sync(model, name: "上書き禁止");
        Assert.Equal("入力保持", editor.Header.ShopName);
        Assert.True(model.IsReadOnly);
        Assert.True(model.ShowRetryMasters);
        Assert.Equal(context.Message, model.RecoveryMessage);
        Assert.Empty(actions);
        Assert.Contains(nameof(model.IsReadOnly), changes);
        await model.RetryMastersCommand.ExecuteAsync(null);
        Assert.Equal(new[] { ReceiptEditorAction.RetryMasters }, actions);
        context = new(false, "", false);
        model.Refresh();
        Assert.Same(editor, model.Editor);
        Assert.Same(parent, model.ParentInput);
        Assert.False(model.IsReadOnly);
        await model.BackCommand.ExecuteAsync(null);
        Assert.Equal(new[] { ReceiptEditorAction.RetryMasters, ReceiptEditorAction.Back }, actions);
    }
    [Fact]
    public void CalculationDisplayProjectsOnlyTheExistingResultWithoutChangingEditorState()
    {
        var model = Model();
        var header = model.Editor.Header;
        var result = ReceiptCalculationDisplay.Evaluate(model.Editor, new TaxRatesInfo([]), new(2026, 11, 19), true);
        model.ShowCalculation(result);
        Assert.Equal("会計合計（反映済み明細）：未計算", model.AccountingTotal);
        Assert.Equal(result.Message, model.CalculationNotice);
        Assert.Same(header, model.Editor.Header);
        Assert.False(model.Editor.NeedsLeaveConfirmation);
        model.ShowCalculation(result with
        {
            Message = null
        });
        Assert.False(model.HasCalculationNotice);
        Assert.Null(model.CalculationNotice);
        model.ShowCalculation(result with
        {
            Message = ""
        });
        Assert.True(model.HasCalculationNotice);
        Assert.Equal("", model.CalculationNotice);
    }
}
