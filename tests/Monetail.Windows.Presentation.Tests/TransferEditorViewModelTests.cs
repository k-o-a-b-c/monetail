using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transfers;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Presentation.Transfers;
using Xunit;

namespace Monetail.Windows.Presentation.Tests;

public sealed class TransferEditorViewModelTests
{
    private static readonly Guid Owner = Guid.NewGuid(), OtherOwner = Guid.NewGuid();
    private static readonly Guid From = Guid.NewGuid(), To = Guid.NewGuid(), Outside = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 11, 19);
    private static PeopleInfo People() => new([
        new(OtherOwner, "他人", null, null, 0, Guid.NewGuid()),
        new(Owner, "本人", null, null, 1, Guid.NewGuid())], Owner, "Admin", Guid.NewGuid());
    private static PaymentMethodsInfo Methods(string role = "Admin") => new([
        new(From, Owner, "現金", 0, null, null, Guid.NewGuid()),
        new(To, Owner, "銀行", 1, null, null, Guid.NewGuid()),
        new(Outside, OtherOwner, "期限外", 0, null, Day.AddDays(-1), Guid.NewGuid())], [], Owner, role);
    private static TransferInfo Saved() => new(Guid.CreateVersion7(), Day.ToDateTime(new(12, 34, 56)), "振替", From, To, 1000, null, Guid.NewGuid());
    private static TransferEditorViewModel Create(TransferEditorState state, Func<TransferEditorAvailability>? availability = null) =>
        new(state, availability ?? (() => default), id => id == From ? "現金" : id == To ? "銀行" : "期限外",
            (_, _) => Task.FromResult(new TransferSuggestionsInfo([], false)),
            (_, _) => Task.FromResult(new TransferAutofillInfo(null, null)));

    [Fact]
    public void InitialPresentationUsesTheSameEditorAndDraftWithoutDirtyingOrDefaultingInput()
    {
        var state = TransferEditorState.Create(Day);
        var draft = state.Draft;
        var model = Create(state);
        model.UpdateMasters(People(), Methods());
        Assert.Same(state, model.Editor);
        Assert.Same(draft, model.Editor.Draft);
        Assert.Equal("振替の登録", model.Title);
        Assert.Equal("登録", model.SaveText);
        Assert.False(model.CanSave);
        Assert.False(model.ShowExistingActions);
        Assert.False(state.HasUnsavedChanges);
        Assert.Null(draft.Amount);
        Assert.Null(draft.From.PaymentMethodId);
        Assert.Null(draft.To.PaymentMethodId);
        Assert.Equal(Day.ToDateTime(TimeOnly.MinValue), draft.TransactionAt);
    }
    [Fact]
    public void RawInputValidationPreservesUnfinishedStringsAndTheOriginalSavedVersion()
    {
        var saved = Saved();
        var state = TransferEditorState.Edit(saved);
        var model = Create(state);
        model.UpdateMasters(People(), Methods());
        model.UpdateInput("2026/", "未確定", "1x", " メモ ");
        Assert.Equal("2026/", state.Draft.UnresolvedDateText);
        Assert.Equal("未確定", state.Draft.UnresolvedTimeText);
        Assert.Equal("1x", state.Draft.UnresolvedAmountText);
        Assert.Equal(" メモ ", state.Draft.Memo);
        Assert.Equal(saved.TransferId, state.TransferId);
        Assert.Equal(saved.Revision, state.Revision);
        Assert.False(model.CanSave);
        Assert.Contains("整数", model.ValidationMessage);
        Assert.Throws<TransferInputException>(() => model.PrepareSave(false));
        Assert.Equal("1x", state.Draft.UnresolvedAmountText);
    }
    [Fact]
    public void SamePaymentErrorAndSaveCommandsClearOnlyWhenTheSelectionIsCorrected()
    {
        var state = TransferEditorState.Edit(Saved());
        var model = Create(state);
        model.UpdateMasters(People(), Methods());
        var changes = 0;
        model.SaveCommand.CanExecuteChanged += (_, _) => changes++;
        state.SelectTo(From);
        model.Refresh();
        Assert.Equal("振替元と振替先は異なる支払方法を選択してください。", model.ToError);
        Assert.False(model.SaveCommand.CanExecute(null));
        Assert.False(model.ReuseCommand.CanExecute(null));
        Assert.Equal(From, state.Draft.To.PaymentMethodId);
        state.SelectTo(To);
        model.Refresh();
        Assert.Equal("", model.ToError);
        Assert.True(model.SaveCommand.CanExecute(null));
        Assert.True(model.ReuseCommand.CanExecute(null));
        Assert.True(changes >= 2);
    }
    [Fact]
    public void ChoicesFilterByDateAndPermissionAndKeepAnOutOfRangeSelectionOnItsOwnSide()
    {
        var state = TransferEditorState.Create(Day);
        state.SelectTo(Outside);
        var model = Create(state);
        model.UpdateMasters(People(), Methods());
        var choices = model.PaymentChoices()!.Value;
        Assert.Equal(new[] { From, To }, choices.From.Select(p => p.Id));
        Assert.Equal(new[] { From, To, Outside }, choices.To.Select(p => p.Id));
        Assert.Equal(Outside, state.Draft.To.PaymentMethodId);
        model.UpdateMasters(People(), Methods("Editor"));
        Assert.Equal(new[] { From, To }, model.PaymentChoices()!.Value.From.Select(p => p.Id));
        model.UpdateInput("未確定", "00:00:00", "", "");
        Assert.Empty(model.PaymentChoices()!.Value.From);
        Assert.Equal(Outside, Assert.Single(model.PaymentChoices()!.Value.To).Id);
    }
    [Fact]
    public void RefreshAndMasterFailureDoNotRewriteInputsOrTheirSelectionSources()
    {
        var state = TransferEditorState.Edit(Saved());
        var model = Create(state);
        model.UpdateMasters(People(), Methods());
        var draft = state.Draft;
        var from = draft.From;
        var to = draft.To;
        var name = model.FromName;
        model.UpdateMasters(null, null);
        model.Refresh();
        Assert.Equal(name, model.FromName);
        Assert.Same(draft, state.Draft);
        Assert.Same(from, draft.From);
        Assert.Same(to, draft.To);
        Assert.False(state.HasUnsavedChanges);
        Assert.False(model.CanSave);
        Assert.Null(model.PaymentChoices());
    }
    [Theory]
    [InlineData(TransferReadOnlyReason.Conflict)]
    [InlineData(TransferReadOnlyReason.Forbidden)]
    [InlineData(TransferReadOnlyReason.NotFound)]
    public void ReadOnlyPresentationProtectsTheSameInputAndDoesNotAdoptConflictData(TransferReadOnlyReason reason)
    {
        var state = TransferEditorState.Edit(Saved());
        state.Draft.Name = "未保存";
        if (reason == TransferReadOnlyReason.Conflict)
            state.RegisterConflict(Saved() with
            {
                TransferId = state.TransferId!.Value,
                Name = "最新"
            });
        else
            state.EnterReadOnly(reason);
        var model = Create(state, () => new(false, false, false, false, false, false, false,
            reason == TransferReadOnlyReason.Conflict, false));
        model.UpdateMasters(People(), Methods());
        model.UpdateInput("2020/01/01", "00:00:00", "1", "上書き");
        Assert.True(model.IsFrozen);
        Assert.False(model.ShowSave);
        Assert.False(model.ShowExistingActions);
        Assert.False(model.CanSave);
        Assert.False(model.CanDelete);
        Assert.Equal("未保存", state.Draft.Name);
        Assert.Equal(1000, state.Draft.Amount);
        Assert.True(model.NoticeOpen);
        Assert.Equal(reason == TransferReadOnlyReason.Conflict, model.ShowLoadLatest);
    }
    [Fact]
    public async Task CommandsOnlyNotifyTheWindowAndKeepSavedIdentityForUpdateAndReuse()
    {
        var saved = Saved();
        var state = TransferEditorState.Edit(saved);
        var model = Create(state, () => new(false, false, false, false, true, true, false, true, false));
        model.UpdateMasters(People(), Methods());
        var actions = new List<TransferEditorAction>();
        model.ActionRequested += action =>
        {
            actions.Add(action);
            return Task.CompletedTask;
        };
        await model.BackCommand.ExecuteAsync(null);
        await model.SaveCommand.ExecuteAsync(null);
        await model.ReuseCommand.ExecuteAsync(null);
        await model.DeleteCommand.ExecuteAsync(null);
        await model.RetryWriteCommand.ExecuteAsync(null);
        await model.RetryMastersCommand.ExecuteAsync(null);
        await model.LoadLatestCommand.ExecuteAsync(null);
        Assert.Equal(Enum.GetValues<TransferEditorAction>(), actions);
        Assert.Equal(saved.TransferId, model.PrepareSave(false).TransferId);
        var reuse = model.PrepareSave(true);
        Assert.Null(reuse.TransferId);
        Assert.Null(reuse.Revision);
        Assert.Equal(saved.Revision, state.Revision);
        Assert.False(state.HasUnsavedChanges);
    }
    [Fact]
    public void WindowAvailabilityIsReadLiveAndOnlyRefreshedForPresentation()
    {
        var current = default(TransferEditorAvailability);
        var state = TransferEditorState.Edit(Saved());
        var model = Create(state, () => current);
        model.UpdateMasters(People(), Methods());
        Assert.True(model.CanSave);
        current = new(true, false, true, false, false, false, true, false, false);
        model.Refresh();
        Assert.True(model.IsFrozen);
        Assert.True(model.IsSending);
        Assert.False(model.CanSave);
        Assert.False(state.HasUnsavedChanges);
        current = new(true, false, false, true, true, false, false, false, false);
        model.Refresh();
        Assert.True(model.ShowSave);
        Assert.True(model.CanRetryWrite);
        Assert.False(model.CanSave);
    }
}
