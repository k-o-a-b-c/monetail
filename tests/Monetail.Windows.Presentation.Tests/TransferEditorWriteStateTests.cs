using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transfers;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Presentation.Transfers;
using Xunit;

namespace Monetail.Windows.Presentation.Tests;

public sealed class TransferEditorWriteStateTests
{
    private static readonly Guid Ledger = Guid.NewGuid(), Owner = Guid.NewGuid();
    private static readonly Guid From = Guid.NewGuid(), To = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 11, 19);
    private static TransferInfo Saved() => new(Guid.CreateVersion7(), Day.ToDateTime(new(12, 0)),
        "振替", From, To, 1200, "メモ", Guid.NewGuid());
    private static TransferEditorViewModel Create(TransferEditorState? editor = null,
        Func<TransferEditorAvailability>? context = null)
    {
        var model = new TransferEditorViewModel(editor ?? TransferEditorState.Edit(Saved()),
            context ?? (() => new(false, false, false, false, true)), _ => "支払方法",
            (_, _) => throw new InvalidOperationException("書込状態の変更で候補を取得しない"),
            (_, _) => throw new InvalidOperationException("書込状態の変更で補完しない"));
        model.UpdateMasters(new([new(Owner, "本人", null, null, 0, Guid.NewGuid())], Owner, "Admin", Guid.NewGuid()),
            new([new(From, Owner, "現金", 0, null, null, Guid.NewGuid()),
                new(To, Owner, "銀行", 1, null, null, Guid.NewGuid())], [], Owner, "Admin"));
        return model;
    }

    [Fact]
    public void PendingWriteAndSnapshotHaveOneOwnerAndNotifyCommandsWithoutChangingTheEditor()
    {
        var model = Create();
        var editor = model.Editor;
        var draft = editor.Draft;
        var revision = editor.Revision;
        var prepared = model.PrepareSave(false);
        var changes = new List<string?>();
        var saveChanges = 0;
        model.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        model.SaveCommand.CanExecuteChanged += (_, _) => saveChanges++;
        Assert.True(model.SaveCommand.CanExecute(null));

        model.PrepareWrite(Ledger, prepared);
        var request = Assert.IsType<TransferWriteRequest>(model.PendingRequest);
        Assert.Same(prepared.Snapshot, model.PendingSnapshot);
        Assert.Equal(TransferWriteOperation.Update, request.Operation);
        Assert.Equal(Ledger, request.LedgerId);
        Assert.Equal(editor.TransferId, request.TransferId);
        Assert.Equal(revision, request.Revision);
        Assert.Equal(4, request.RequestId.Version);
        Assert.True(model.IsFrozen);
        Assert.False(model.SaveCommand.CanExecute(null));
        Assert.False(model.DeleteCommand.CanExecute(null));
        Assert.False(model.ReuseCommand.CanExecute(null));
        Assert.False(model.RetryWriteCommand.CanExecute(null));

        model.StartSending();
        Assert.True(model.IsSending);
        model.FinishSending();
        Assert.False(model.IsSending);
        Assert.Same(request, model.PendingRequest);
        Assert.Same(editor, model.Editor);
        Assert.Same(draft, editor.Draft);
        Assert.Equal(revision, editor.Revision);
        Assert.False(editor.HasUnsavedChanges);
        Assert.Contains(nameof(model.PendingRequest), changes);
        Assert.Contains(nameof(model.IsSending), changes);
        Assert.Contains(nameof(model.IsFrozen), changes);
        Assert.True(saveChanges >= 3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthenticationKeepsTheSameRequestAndPriorUncertaintyUntilAnExplicitRetry(bool uncertain)
    {
        var context = new TransferEditorAvailability(false, false, false, false, true);
        var model = Create(context: () => context);
        var prepared = model.PrepareSave(false);
        model.PrepareWrite(Ledger, prepared);
        var request = model.PendingRequest;
        var notifications = new List<string?>();
        var actions = new List<TransferEditorAction>();
        model.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        model.ActionRequested += action =>
        {
            actions.Add(action);
            return Task.CompletedTask;
        };
        if (uncertain)
        {
            model.StartSending();
            model.SetUncertainResult(true);
            model.FinishSending();
        }
        var wasUnknown = model.IsUnknown;
        model.StartSending();
        Assert.False(model.RetryWriteCommand.CanExecute(null));
        model.SetUncertainResult(wasUnknown);
        context = context with
        {
            RecoveryBlocked = true,
            HasAccount = false
        };
        model.RememberAuthentication();
        model.FinishSending();
        Assert.Equal(uncertain, model.IsUnknown);
        Assert.Equal(uncertain, model.HasUnknownWrite);
        Assert.True(model.RetryAfterLogin);
        Assert.False(model.CanRetryWrite);

        // 連続401・マスタ失敗では元の要求と未知結果を変えず、取得成功を待つ。
        model.RememberAuthentication();
        context = context with
        {
            HasAccount = true,
            MastersFailed = true
        };
        model.Refresh();
        Assert.False(model.CanRetryWrite);
        context = context with
        {
            RecoveryBlocked = false,
            MastersFailed = false
        };
        model.Refresh();
        Assert.True(model.RetryWriteCommand.CanExecute(null));
        Assert.Empty(actions);
        Assert.Same(request, model.PendingRequest);
        Assert.Same(prepared.Snapshot, model.PendingSnapshot);
        Assert.Contains(nameof(model.RetryAfterLogin), notifications);
        Assert.Contains(nameof(model.IsUnknown), notifications);
        await model.RetryWriteCommand.ExecuteAsync(null);
        Assert.Equal(new[] { TransferEditorAction.RetryWrite }, actions);
        Assert.Same(request, model.PendingRequest);
        model.StartSending();
        Assert.False(model.IsUnknown);
        Assert.False(model.RetryAfterLogin);
        Assert.False(model.CanRetryWrite);
        Assert.Same(request, model.PendingRequest);
    }

    [Fact]
    public void UnknownResultDoesNotReleaseTheSnapshotOrAllowAnotherSave()
    {
        var model = Create();
        model.PrepareWrite(Ledger, model.PrepareSave(true));
        var request = model.PendingRequest;
        var snapshot = model.PendingSnapshot;
        model.StartSending();
        model.SetUncertainResult(true);
        Assert.False(model.CanRetryWrite);
        model.FinishSending();
        Assert.True(model.HasUnknownWrite);
        Assert.True(model.CanRetryWrite);
        Assert.False(model.CanAdoptLatest);
        Assert.False(model.CanSave);
        Assert.False(model.CanReuse);
        Assert.False(model.CanDelete);
        Assert.Equal(TransferWriteOperation.Create, request!.Operation);
        Assert.Null(request.TransferId);
        Assert.Null(request.Revision);
        model.Refresh();
        Assert.Same(request, model.PendingRequest);
        Assert.Same(snapshot, model.PendingSnapshot);
    }

    [Theory]
    [InlineData(TransferReadOnlyReason.Conflict)]
    [InlineData(TransferReadOnlyReason.Forbidden)]
    [InlineData(TransferReadOnlyReason.NotFound)]
    public void DefiniteErrorsReleaseOnlyTheWriteAndKeepInputsAndOldRevision(TransferReadOnlyReason reason)
    {
        var model = Create();
        var editor = model.Editor;
        var revision = editor.Revision;
        editor.Draft.Name = "未保存";
        model.PrepareWrite(Ledger, model.PrepareSave(false));
        model.StartSending();
        model.ClearPendingWrite();
        if (reason == TransferReadOnlyReason.Conflict)
            editor.RegisterConflict(Saved() with
            {
                TransferId = editor.TransferId!.Value
            });
        else
            editor.EnterReadOnly(reason);
        model.FinishSending();
        Assert.Null(model.PendingRequest);
        Assert.Null(model.PendingSnapshot);
        Assert.False(model.IsUnknown);
        Assert.False(model.CanRetryWrite);
        Assert.False(model.CanSave);
        Assert.False(model.CanReuse);
        Assert.False(model.CanDelete);
        Assert.Equal(reason == TransferReadOnlyReason.Conflict, model.CanAdoptLatest);
        Assert.Equal(reason == TransferReadOnlyReason.Conflict, model.LoadLatestCommand.CanExecute(null));
        Assert.Equal("未保存", editor.Draft.Name);
        Assert.Equal(revision, editor.Revision);
    }

    [Fact]
    public void RequestConflictStopsAllWritingAndDoesNotOfferRevisionRecovery()
    {
        var model = Create();
        model.PrepareWrite(Ledger, model.PrepareSave(false));
        model.StartSending();
        model.ClearPendingWrite();
        model.StopWriting();
        model.FinishSending();
        Assert.True(model.IsStopped);
        Assert.True(model.IsFrozen);
        Assert.False(model.ShowSave);
        Assert.False(model.ShowExistingActions);
        Assert.False(model.CanRetryWrite);
        Assert.False(model.CanAdoptLatest);
    }

    [Fact]
    public void ConfirmedFailureCanBeCorrectedWithANewRequestWhileSuccessKeepsTheOriginalSnapshotForNavigation()
    {
        var model = Create();
        model.PrepareWrite(Ledger, model.PrepareSave(false));
        var previous = model.PendingRequest;
        model.StartSending();
        model.ClearPendingWrite();
        model.FinishSending();
        Assert.True(model.CanSave);
        model.Editor.Draft.SetAmount(1500);
        model.PrepareWrite(Ledger, model.PrepareSave(false));
        Assert.NotEqual(previous!.RequestId, model.PendingRequest!.RequestId);
        model.StartSending();
        model.FinishSending();
        var savedSnapshot = model.PendingSnapshot;
        model.ClearPendingWrite();
        Assert.Equal(1500, savedSnapshot!.Amount);
        Assert.Null(model.PendingSnapshot);
        Assert.Null(model.PendingRequest);
    }

    [Fact]
    public void DeleteKeepsItsIdentityWithoutASnapshotAndDiscardClearsEveryWriteState()
    {
        var model = Create();
        model.PrepareDeleteWrite(Ledger);
        Assert.Equal(TransferWriteOperation.Delete, model.PendingRequest!.Operation);
        Assert.Equal(model.Editor.TransferId, model.PendingRequest.TransferId);
        Assert.Equal(model.Editor.Revision, model.PendingRequest.Revision);
        Assert.Null(model.PendingSnapshot);
        model.SetPreparingSave(true);
        Assert.True(model.IsPreparingSave);
        model.StartSending();
        model.SetUncertainResult(true);
        model.RememberAuthentication();
        model.StopWriting();
        model.DiscardWrite();
        model.DiscardWrite();
        Assert.Null(model.PendingRequest);
        Assert.Null(model.PendingSnapshot);
        Assert.False(model.IsSending);
        Assert.False(model.IsUnknown);
        Assert.False(model.IsStopped);
        Assert.False(model.RetryAfterLogin);
        Assert.False(model.IsPreparingSave);
        Assert.True(model.CanSave);
        Assert.False(model.Editor.HasUnsavedChanges);
    }

    [Fact]
    public void WindowContextBlocksRetryAndLatestWithoutDuplicatingWriteFlags()
    {
        var context = new TransferEditorAvailability(false, false, false, false, true);
        var model = Create(context: () => context);
        model.PrepareDeleteWrite(Ledger);
        model.SetUncertainResult(true);
        Assert.True(model.CanRetryWrite);
        context = context with
        {
            DialogOpen = true
        };
        model.Refresh();
        Assert.False(model.RetryWriteCommand.CanExecute(null));
        context = context with
        {
            DialogOpen = false,
            HasAccount = false
        };
        model.Refresh();
        Assert.False(model.CanRetryWrite);
        context = context with
        {
            HasAccount = true,
            RecoveryBlocked = true
        };
        model.Refresh();
        Assert.False(model.CanRetryWrite);
        Assert.True(model.HasUnknownWrite);
        Assert.NotNull(model.PendingRequest);
    }
}
