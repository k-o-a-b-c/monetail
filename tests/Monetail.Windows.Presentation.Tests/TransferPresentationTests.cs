using System.Collections.Specialized;
using System.Globalization;
using System.Net;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Presentation.Transfers;
using Xunit;
using static Monetail.Windows.Presentation.Tests.TransferListViewModelTests;

namespace Monetail.Windows.Presentation.Tests;

public sealed class TransferPresentationTests
{
    [Fact]
    public async Task ViewportFillChangeNotifiesOnlyDependentPropertiesAndContinueCommand()
    {
        var calls = 0;
        using var model = Create((_, _) =>
            Task.FromResult(new TransferListInfo(Rows(calls++ * 100, 100), "next")));
        await model.StartAsync(new("all"));
        await model.ObserveViewportAsync(99, false, false);
        await model.ObserveViewportAsync(199, false, false);
        Assert.True(model.CanContinue);

        var changed = new List<string?>();
        var commandChanges = 0;
        model.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        model.ContinueCommand.CanExecuteChanged += (_, _) => commandChanges++;

        await model.ObserveViewportAsync(299, true, false);
        Assert.Equal(new[] { nameof(model.ViewportFilled), nameof(model.CanContinue) }, changed);
        Assert.False(model.ContinueCommand.CanExecute(null));
        Assert.Equal(1, commandChanges);

        changed.Clear();
        await model.ObserveViewportAsync(299, true, false);
        Assert.Empty(changed);
        Assert.Equal(1, commandChanges);

        await model.ObserveViewportAsync(299, false, false);
        Assert.Equal(new[] { nameof(model.ViewportFilled), nameof(model.CanContinue) }, changed);
        Assert.True(model.ContinueCommand.CanExecute(null));
        Assert.Equal(2, commandChanges);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task SharedSearchRejectsInvalidMonthWithoutFetchingOrChangingAppliedFilters()
    {
        var search = new RecordSearchState(new(2026, 12, 31));
        Assert.True(search.ApplySharedText("確定"));
        search.Receipts.MonthText = "未確定年月";
        var reads = 0;
        using var model = new TransferListViewModel((_, _) =>
        {
            reads++;
            return Task.FromResult(new TransferListInfo([], null));
        }, search, _ => "現金");
        var draft = " 未確定ＡＴＭ ";
        if (search.ApplySharedText(draft))
            await model.StartAsync(search.TransferQuery());
        Assert.Equal(0, reads);
        Assert.Equal(" 未確定ＡＴＭ ", draft);
        Assert.Equal("確定", search.TransferQuery().Q);
        Assert.Equal("確定", search.Receipts.Query().Q);
        Assert.Equal("未確定年月", search.Receipts.MonthText);
        Assert.NotNull(search.Receipts.MonthError);
    }
    [Fact]
    public async Task OpeningCommandsOnlyNotifyAndUseTheExternallyDeterminedRegistrationPermission()
    {
        var reads = 0;
        using var model = Create((_, _) => { reads++; return Task.FromResult(new TransferListInfo([], null)); });
        var opened = new List<Guid?>();
        model.OpenRequested += id => { opened.Add(id); return Task.CompletedTask; };
        Assert.False(model.RegisterCommand.CanExecute(null));
        var canExecuteChanges = 0;
        model.RegisterCommand.CanExecuteChanged += (_, _) => canExecuteChanges++;
        model.CanRegister = true;
        Assert.True(model.RegisterCommand.CanExecute(null));
        await model.RegisterCommand.ExecuteAsync(null);
        var id = Guid.NewGuid();
        await model.EditCommand.ExecuteAsync(id);
        model.CanRegister = false;
        Assert.False(model.RegisterCommand.CanExecute(null));
        Assert.Equal(new Guid?[] { null, id }, opened);
        Assert.True(canExecuteChanges >= 2);
        Assert.Equal(0, reads);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpeningCommandCanRestartAfterCancellationWithoutWaitingForOldTask(bool edit)
    {
        using var model = Create((_, _) => Task.FromResult(new TransferListInfo([], null)));
        var target = edit ? Guid.NewGuid() : (Guid?)null;
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var opened = new List<Guid?>();
        model.OpenRequested += id =>
        {
            opened.Add(id);
            return opened.Count == 1 ? pending.Task : Task.CompletedTask;
        };
        Func<Task> execute = edit
            ? () => model.EditCommand.ExecuteAsync(target!.Value)
            : () => model.RegisterCommand.ExecuteAsync(null);
        Func<bool> canExecute = edit
            ? () => model.EditCommand.CanExecute(target!.Value)
            : () => model.RegisterCommand.CanExecute(null);
        model.CanRegister = true;
        var first = execute();
        Assert.True(canExecute());
        // Windowが古い入力要求を取り消して入口を戻したとき、Commandの実行状態で閉塞しない。
        model.CanRegister = false;
        if (!edit)
            Assert.False(canExecute());
        model.CanRegister = true;
        Assert.True(canExecute());
        await execute();
        Assert.Equal(new Guid?[] { target, target }, opened);
        Assert.False(first.IsCompleted);
        pending.SetResult();
        await first;
    }

    [Fact]
    public async Task TheReadOnlyCollectionHasOneLifetimeAndSelectionUsesTheSameItemInstances()
    {
        using var model = Create((_, _) => Task.FromResult(new TransferListInfo(Rows(0, 1), null)));
        var rows = model.Rows;
        var notices = new List<NotifyCollectionChangedAction>();
        ((INotifyCollectionChanged)rows).CollectionChanged += (_, e) => notices.Add(e.Action);
        Assert.True(((ICollection<TransferListItem>)rows).IsReadOnly);
        await model.StartAsync(new("all"));
        model.SelectedItem = rows[0];
        Assert.Same(rows[0], model.SelectedItem);
        Assert.Equal(rows[0].TransferId, model.SelectedTransferId);
        await model.StartAsync(new("2026-12"));
        Assert.Same(rows, model.Rows);
        Assert.Null(model.SelectedItem);
        Assert.Contains(NotifyCollectionChangedAction.Reset, notices);
    }
    [Fact]
    public async Task LoadingFailureEmptyAndRetryCommandsNotifyWithoutLeakingServerMessages()
    {
        var pending = new TaskCompletionSource<TransferListInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var model = Create((_, _) => ++calls == 1 ? pending.Task : Task.FromResult(new TransferListInfo([], null)));
        var names = new List<string?>();
        model.PropertyChanged += (_, e) => names.Add(e.PropertyName);
        var read = model.StartAsync(new("all"));
        Assert.True(model.IsInitialLoading);
        Assert.False(model.RetryCommand.CanExecute(null));
        pending.SetException(new TransferRequestException(HttpStatusCode.Forbidden, "forbidden", null));
        await read;
        Assert.True(model.IsInitialFailure);
        Assert.Equal("この操作を行う権限がありません。", model.ErrorText);
        Assert.True(model.RetryCommand.CanExecute(null));
        await model.RetryCommand.ExecuteAsync(null);
        Assert.True(model.IsEmpty);
        Assert.False(model.IsLoading);
        Assert.False(model.IsInitialFailure);
        Assert.False(model.RetryCommand.CanExecute(null));
        Assert.Equal(2, calls);
        Assert.Contains(nameof(model.IsInitialLoading), names);
        Assert.Contains(nameof(model.IsEmpty), names);
        Assert.Contains(nameof(model.CanRetry), names);
    }
    [Fact]
    public async Task ContinueAndReturnCommandsPreserveThePagingAndFocusContracts()
    {
        var calls = new List<TransferListQuery>();
        using var model = Create((q, _) =>
        {
            calls.Add(q);
            return Task.FromResult(new TransferListInfo(Rows((calls.Count - 1) * 100, 100), "next"));
        });
        await model.StartAsync(new("all", focusTransferId: Guid.NewGuid()));
        Assert.True(model.IsFocused);
        await model.ObserveViewportAsync(99, false, false);
        await model.ObserveViewportAsync(199, false, false);
        Assert.True(model.ContinueCommand.CanExecute(null));
        await model.ContinueCommand.ExecuteAsync(null);
        Assert.Equal(400, model.Rows.Count);
        await model.ReturnToStartCommand.ExecuteAsync(null);
        Assert.False(model.IsFocused);
        Assert.Null(calls[^1].FocusTransferId);
        Assert.Null(calls[^1].Cursor);
        Assert.Equal(100, model.Rows.Count);
    }
    [Fact]
    public async Task MasterNameRefreshChangesOnlyNamesAndRetainsRowsSelectionAmountsAndNoAdditionalGet()
    {
        var name = "現金";
        var reads = 0;
        var saved = Rows(0, 1)[0] with
        {
            Amount = 12345,
            Memo = "保存済み"
        };
        using var model = new TransferListViewModel((_, _) => { reads++; return Task.FromResult(new TransferListInfo([saved], null)); }, new(new(2026, 11, 10)), _ => name);
        await model.StartAsync(new("all"));
        model.SelectedItem = model.Rows[0];
        var row = model.SelectedItem;
        var changes = new List<string?>();
        row.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        Assert.Equal("2026/11/10 00:00:00", row.TransactionAt);
        Assert.Equal(12345.ToString("N0", CultureInfo.CurrentCulture), row.Amount);
        name = "（母）銀行";
        model.RefreshNames();
        Assert.Same(row, model.SelectedItem);
        Assert.Same(saved, row.Transfer);
        Assert.Equal("（母）銀行", row.FromName);
        Assert.Equal("（母）銀行", row.ToName);
        Assert.Equal("保存済み", row.Memo);
        Assert.Equal(new[] { nameof(row.FromName), nameof(row.ToName) }, changes);
        Assert.Equal(1, reads);
    }
    [Fact]
    public async Task AuthenticationFailureIsRetainedForTheShellWithoutAutomaticRetryOrNavigation()
    {
        var calls = 0;
        var failure = new AuthenticationRejectedException();
        using var model = Create((_, _) => { calls++; throw failure; });
        await model.StartAsync(new("all"));
        Assert.Same(failure, model.Failure);
        await model.ObserveViewportAsync(0, false, true);
        await model.ResumeAsync();
        Assert.Equal(1, calls);
        Assert.True(model.IsInitialFailure);
    }
    [Fact]
    public async Task FailedSavedDiscoveryCanBeExplicitlyRetriedWithoutAnOutsideSavedSelectionField()
    {
        var calls = 0;
        var saved = Rows(100, 1)[0];
        using var model = Create((q, _) => ++calls == 2 ? Task.FromException<TransferListInfo>(new HttpRequestException())
            : Task.FromResult(q.Cursor is null ? new TransferListInfo(Rows(0, 100), "next") : new TransferListInfo([saved], null)));
        await model.ShowSavedAsync(new("all"), saved.TransferId);
        Assert.True(model.IsContinuationFailure);
        Assert.False(model.IsFindingSaved);
        await model.RetryCommand.ExecuteAsync(null);
        Assert.Equal(saved.TransferId, model.SelectedItem?.TransferId);
        Assert.False(model.IsFindingSaved);
        Assert.Equal(4, calls);
    }
    [Fact]
    public async Task SavedDiscoveryResumesTheInterruptedCursorAndKeepsTheThreePageBudget()
    {
        var queries = new List<TransferListQuery>();
        var pending = new TaskCompletionSource<TransferListInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var model = Create((q, _) =>
        {
            queries.Add(q);
            return queries.Count == 2 ? pending.Task
                : Task.FromResult(new TransferListInfo(Rows(q.Cursor is null ? 0 : 100, 100), "next"));
        });
        var id = Rows(100, 1)[0].TransferId;
        var showing = model.ShowSavedAsync(new("all"), id);
        model.Suspend();
        pending.SetResult(new(Rows(100, 100), "stale"));
        await showing;
        Assert.Equal(100, model.Rows.Count);
        await model.ResumeAsync();
        Assert.Equal("next", queries[^1].Cursor);
        Assert.Equal(id, model.SelectedTransferId);
        Assert.False(model.IsFindingSaved);
        Assert.Equal(1, model.AutomaticFillCount);
        Assert.Equal(3, queries.Count);
    }
    [Fact]
    public async Task ResumedFocus404FallsBackOnceEvenWhenTheReplacementRequestCompletesSynchronously()
    {
        var queries = new List<TransferListQuery>();
        var pending = new TaskCompletionSource<TransferListInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        var target = Rows(500, 1)[0].TransferId;
        using var model = Create((q, _) =>
        {
            queries.Add(q);
            if (queries.Count == 4)
                return pending.Task;
            if (q.FocusTransferId is not null)
                throw new TransferRequestException(HttpStatusCode.NotFound, "not_found", null);
            return Task.FromResult(queries.Count > 4 ? new TransferListInfo([], null)
                : new TransferListInfo(Rows((queries.Count - 1) * 100, 100), "next"));
        });
        var showing = model.ShowSavedAsync(new("all"), target);
        model.Suspend();
        pending.SetException(new TransferRequestException(HttpStatusCode.NotFound, "not_found", null));
        await showing;
        await model.ResumeAsync();
        Assert.Equal(6, queries.Count);
        Assert.Equal(2, queries.Count(q => q.FocusTransferId == target));
        Assert.Null(model.FocusTransferId);
        Assert.Null(model.Failure);
        Assert.Null(model.SelectedItem);
        Assert.False(model.IsFindingSaved);
        Assert.Empty(model.Rows);
    }

    [Fact]
    public async Task SharedSearchCanReplaceAPendingReadAndRejectTheOldReply()
    {
        var search = new RecordSearchState(new(2026, 11, 10));
        var pending = new TaskCompletionSource<TransferListInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queries = new List<TransferListQuery>();
        using var model = new TransferListViewModel((q, _) =>
        {
            queries.Add(q);
            return q.Q == "old" ? pending.Task : Task.FromResult(new TransferListInfo(Rows(100, 1), null));
        }, search, _ => "現金");
        Assert.True(search.ApplySharedText("old"));
        var old = model.StartAsync(search.TransferQuery());
        Assert.True(model.IsInitialLoading);
        Assert.True(search.ApplySharedText("new"));
        await model.StartAsync(search.TransferQuery());
        pending.SetResult(new(Rows(0, 100), "stale"));
        await old;
        Assert.Equal(new[] { "old", "new" }, queries.Select(q => q.Q));
        Assert.Single(model.Rows);
        Assert.Equal(Rows(100, 1)[0].TransferId, model.Rows[0].TransferId);
        Assert.Null(model.NextCursor);
        Assert.Equal("new", search.TransferQuery().Q);
        Assert.Equal("new", search.Receipts.Query().Q);
    }

}
