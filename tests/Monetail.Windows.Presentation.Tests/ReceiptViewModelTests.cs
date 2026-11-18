using System.Collections.Specialized;
using System.Net;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Presentation.Receipts;
using Xunit;

namespace Monetail.Windows.Presentation.Tests;

public sealed class ReceiptViewModelTests
{
    [Fact]
    public async Task OneCollectionSelectionAndOriginalSearchTextHaveOneOwner()
    {
        var search = new RecordSearchState(new(2026, 11, 18));
        var info = new ReceiptSummaryInfo(Guid.NewGuid(), new(2026, 11, 18, 12, 34, 56), " 店舗 ", null, Guid.NewGuid(), -1234, null);
        using var model = new ReceiptListViewModel((_, _) => Task.FromResult(new ReceiptListInfo([info], null)), search, _ => "現金");
        var rows = model.Rows;
        var actions = new List<NotifyCollectionChangedAction>();
        ((INotifyCollectionChanged)rows).CollectionChanged += (_, args) => actions.Add(args.Action);
        await model.StartAsync(new("all"));
        var item = Assert.Single(rows);
        Assert.Same(rows, model.Rows);
        Assert.Same(info, item.Receipt);
        Assert.Equal(" 店舗 ", item.ShopName);
        Assert.Equal("", item.ShopLocation);
        Assert.Equal("", item.Memo);
        model.SelectedItem = item;
        Assert.Equal(info.ReceiptId, model.SelectedReceiptId);
        model.SearchText = " ＡＢＣ  ";
        Assert.Equal(model.SearchText, search.Receipts.ShopText);
        Assert.Equal("", search.Receipts.Query().Q);
        await model.StartAsync(new("2026-11"));
        Assert.Null(model.SelectedItem);
        Assert.Null(model.SelectedReceiptId);
        Assert.Equal(new[] { NotifyCollectionChangedAction.Reset, NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Reset, NotifyCollectionChangedAction.Add }, actions);
    }

    [Fact]
    public async Task CommandsNotifyAndForwardSearchAndOpenWithoutOwningNavigation()
    {
        using var model = new ReceiptListViewModel((_, _) => Task.FromResult(new ReceiptListInfo([], null)), new(new(2026, 11, 18)), _ => "現金");
        var notifications = 0;
        model.RegisterCommand.CanExecuteChanged += (_, _) => notifications++;
        Assert.False(model.RegisterCommand.CanExecute(null));
        model.CanRegister = true;
        Assert.True(model.RegisterCommand.CanExecute(null));
        Assert.Equal(1, notifications);
        var opened = new List<Guid?>();
        var searches = 0;
        model.OpenRequested += id => { opened.Add(id); return Task.CompletedTask; };
        model.SearchRequested += () => { searches++; return Task.CompletedTask; };
        await model.SearchCommand.ExecuteAsync(null);
        await model.RegisterCommand.ExecuteAsync(null);
        var id = Guid.NewGuid();
        await model.EditCommand.ExecuteAsync(id);
        Assert.Equal(1, searches);
        Assert.Equal(new Guid?[] { null, id }, opened);
    }
    [Fact]
    public async Task PendingSearchCanBeReplacedThroughTheCommandAndOldReplyIsRejected()
    {
        var search = new RecordSearchState(new(2026, 11, 18));
        var old = new TaskCompletionSource<ReceiptListInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queries = new List<ReceiptListQuery>();
        var cancelled = false;
        using var model = new ReceiptListViewModel((query, token) =>
        {
            queries.Add(query);
            if (query.Q == "old")
            {
                token.Register(() => cancelled = true);
                return old.Task;
            }
            return Task.FromResult(new ReceiptListInfo(ReceiptListViewModelTests.Rows(500, 1), null));
        }, search, _ => "現金");
        model.SearchRequested += () =>
        {
            Assert.True(search.Receipts.ApplyText());
            return model.StartAsync(search.Receipts.Query());
        };
        model.SearchText = "old";
        var pending = model.SearchCommand.ExecuteAsync(null);
        Assert.True(model.SearchCommand.CanExecute(null));
        model.SearchText = "new";
        await model.SearchCommand.ExecuteAsync(null);
        Assert.True(cancelled);
        old.SetResult(new(ReceiptListViewModelTests.Rows(0, 100), "old-cursor"));
        await pending;
        Assert.Equal(new[] { "old", "new" }, queries.Select(query => query.Q));
        Assert.Equal(ReceiptListViewModelTests.Rows(500, 1)[0].ReceiptId, Assert.Single(model.Rows).ReceiptId);
        Assert.Null(model.NextCursor);
        Assert.Equal("new", model.SearchText);
    }

    [Fact]
    public async Task FailureAndRetryCommandNotifyWithoutExposingMessagesOrRetryingAutomatically()
    {
        var failure = new ReceiptRequestException(HttpStatusCode.TooManyRequests, "limited", null);
        var calls = 0;
        using var model = ReceiptListViewModelTests.Create((_, _) => ++calls == 1
            ? Task.FromException<ReceiptListInfo>(failure) : Task.FromResult(new ReceiptListInfo([], null)));
        var properties = new List<string?>();
        var retryNotifications = 0;
        model.PropertyChanged += (_, args) => properties.Add(args.PropertyName);
        model.RetryCommand.CanExecuteChanged += (_, _) => retryNotifications++;
        Assert.False(model.RetryCommand.CanExecute(null));
        await model.StartAsync(new("all"));
        Assert.Same(failure, model.Failure);
        Assert.True(model.IsInitialFailure);
        Assert.True(model.RetryCommand.CanExecute(null));
        Assert.Contains("操作回数", model.ErrorText);
        Assert.Contains(nameof(model.IsLoading), properties);
        Assert.Contains(nameof(model.ErrorText), properties);
        await model.ObserveViewportAsync(99, false, true);
        Assert.Equal(1, calls);
        await model.RetryCommand.ExecuteAsync(null);
        Assert.Equal(2, calls);
        Assert.True(model.IsEmpty);
        Assert.Null(model.Failure);
        Assert.False(model.RetryCommand.CanExecute(null));
        Assert.True(retryNotifications > 0);
    }

    [Fact]
    public async Task AuthenticationFailureRemainsForTheShellWithoutNavigationOrAutomaticRetry()
    {
        var failure = new AuthenticationRejectedException();
        var calls = 0;
        using var model = ReceiptListViewModelTests.Create((_, _) =>
        {
            calls++;
            return Task.FromException<ReceiptListInfo>(failure);
        });
        await model.StartAsync(new("all"));
        model.Suspend();
        await model.ResumeAsync();
        await model.ObserveViewportAsync(0, false, true);
        Assert.Same(failure, model.Failure);
        Assert.True(model.IsInitialFailure);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RefreshNamesAndExternalDraftNotificationPreserveRowsSelectionAndStoredValues()
    {
        var search = new RecordSearchState(new(2026, 11, 18));
        var name = "旧名称";
        var calls = 0;
        var source = ReceiptListViewModelTests.Rows(0, 1)[0];
        using var model = new ReceiptListViewModel((_, _) =>
        {
            calls++;
            return Task.FromResult(new ReceiptListInfo([source], null));
        }, search, _ => name);
        await model.StartAsync(new("all"));
        var item = Assert.Single(model.Rows);
        model.SelectedItem = item;
        var properties = new List<string?>();
        item.PropertyChanged += (_, args) => properties.Add(args.PropertyName);
        name = "新名称";
        model.RefreshNames();
        Assert.Equal(new[] { nameof(item.PaymentName) }, properties);
        Assert.Equal(name, item.PaymentName);
        Assert.Same(source, item.Receipt);
        Assert.Same(item, model.SelectedItem);
        search.Receipts.ShopText = " 外部の下書き ";
        var notifications = new List<string?>();
        model.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        model.RefreshSearchText();
        Assert.Equal(" 外部の下書き ", model.SearchText);
        Assert.Equal("", search.Receipts.Query().Q);
        Assert.Contains(nameof(model.SearchText), notifications);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ForwardedOpenCommandsDoNotIntroduceAnotherPendingRequestGuard()
    {
        using var model = ReceiptListViewModelTests.Create((_, _) => Task.FromResult(new ReceiptListInfo([], null)));
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        model.CanRegister = true;
        model.OpenRequested += _ => ++calls == 1 ? pending.Task : Task.CompletedTask;
        var first = model.RegisterCommand.ExecuteAsync(null);
        model.CanRegister = false;
        Assert.False(model.RegisterCommand.CanExecute(null));
        // Windowが旧要求を取り消して入口を戻した後は、旧HTTP応答を待って閉塞しない。
        model.CanRegister = true;
        Assert.True(model.RegisterCommand.CanExecute(null));
        await model.RegisterCommand.ExecuteAsync(null);
        Assert.Equal(2, calls);
        pending.SetResult();
        await first;
    }
    [Fact]
    public async Task DisposalClearsTheOwnedRowsAndSelectionAndRejectsDelayedPages()
    {
        var pending = new TaskCompletionSource<ReceiptListInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var model = ReceiptListViewModelTests.Create((_, _) => ++calls == 1
            ? Task.FromResult(new ReceiptListInfo(ReceiptListViewModelTests.Rows(0, 100), "next")) : pending.Task);
        await model.StartAsync(new("all"));
        model.SelectedItem = model.Rows[0];
        var rows = model.Rows;
        var read = model.ObserveViewportAsync(99, true, true);
        model.Dispose();
        Assert.Same(rows, model.Rows);
        Assert.Empty(rows);
        Assert.Null(model.SelectedItem);
        Assert.Null(model.SelectedReceiptId);
        Assert.False(model.IsLoading);
        pending.SetResult(new(ReceiptListViewModelTests.Rows(100, 100), "old"));
        await read;
        Assert.Empty(rows);
        Assert.Null(model.SelectedItem);
    }

    [Fact]
    public async Task ViewportChangesNotifyOnlyTheViewportAndContinueAvailability()
    {
        var reads = 0;
        using var model = ReceiptListViewModelTests.Create((_, _) =>
        {
            reads++;
            return Task.FromResult(new ReceiptListInfo(ReceiptListViewModelTests.Rows(0, 100), "next"));
        });
        await model.StartAsync(new("all"));
        model.CanRegister = true;
        model.SelectedItem = model.Rows[0];
        var selected = model.SelectedItem;
        var properties = new List<string?>();
        var retries = 0;
        var continues = 0;
        model.PropertyChanged += (_, args) => properties.Add(args.PropertyName);
        model.RetryCommand.CanExecuteChanged += (_, _) => retries++;
        model.ContinueCommand.CanExecuteChanged += (_, _) => continues++;
        await model.ObserveViewportAsync(0, true, false);
        Assert.Equal(new[] { nameof(model.ViewportFilled), nameof(model.CanContinue) }, properties);
        Assert.Equal(1, continues);
        Assert.Equal(0, retries);
        Assert.Equal(1, reads);
        Assert.True(model.CanRegister);
        Assert.Same(selected, model.SelectedItem);
    }
}
