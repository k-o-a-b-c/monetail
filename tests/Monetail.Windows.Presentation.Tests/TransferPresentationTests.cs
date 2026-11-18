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
    public async Task SearchProxiesTheSingleSharedDraftAndRequestsCommitWithoutFetchingOrChangingFilters()
    {
        var search = new RecordSearchState(new(2026, 12, 31));
        search.TransferText = "確定";
        Assert.True(search.ApplyTransferText());
        search.Receipts.MonthText = "未確定年月";
        search.Receipts.ShopText = "店の下書き";
        var reads = 0;
        using var model = new TransferListViewModel((_, _) => { reads++; return Task.FromResult(new TransferListInfo([], null)); }, search, _ => "現金");
        var notified = new List<string?>();
        model.PropertyChanged += (_, e) => notified.Add(e.PropertyName);
        model.SearchText = " 未確定ＡＴＭ ";
        Assert.Equal(model.SearchText, search.TransferText);
        Assert.Equal("確定", search.TransferQuery().Q);
        var committed = 0;
        model.SearchRequested += () => { committed++; Assert.False(search.ApplyTransferText()); return Task.CompletedTask; };
        await model.SearchCommand.ExecuteAsync(null);
        Assert.Equal(1, committed);
        Assert.Equal(0, reads);
        Assert.Equal(" 未確定ＡＴＭ ", model.SearchText);
        Assert.Equal("未確定年月", search.Receipts.MonthText);
        Assert.Equal("店の下書き", search.Receipts.ShopText);
        search.TransferText = "外部の変更";
        model.RefreshSearchText();
        Assert.Equal("外部の変更", model.SearchText);
        Assert.Equal(2, notified.Count(n => n == nameof(model.SearchText)));
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
}
