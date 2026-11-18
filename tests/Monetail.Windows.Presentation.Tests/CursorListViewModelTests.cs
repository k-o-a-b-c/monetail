using Monetail.Windows.Presentation.Lists;
using Xunit;

namespace Monetail.Windows.Presentation.Tests;

public sealed class CursorListViewModelTests
{
    private sealed class InvalidCursor : Exception;
    private sealed class ListModel(Func<string?, CancellationToken, Task<CursorPage<string>>> load)
        : CursorListViewModel<string>
    {
        private bool started;
        protected override bool HasQuery => started;
        public override string ErrorText => "取得失敗";
        public Task StartAsync()
        {
            ResetPages();
            started = true;
            return ReadAsync(false, default);
        }
        protected override Task<CursorPage<string>> LoadPageAsync(string? cursor, CancellationToken token) => load(cursor, token);
        protected override ReadFailure ClassifyFailure(Exception error, string? cursor) => error switch
        {
            InvalidCursor => ReadFailure.InvalidCursor,
            IOException => ReadFailure.Failure,
            _ => ReadFailure.Unhandled
        };
    }
    private static CursorPage<string> Page(int offset) => new(Enumerable.Range(offset, 100).Select(i => i.ToString()), (offset + 100).ToString());

    [Fact]
    public async Task SharedCollectionSelectionBudgetAndCommandsHaveOneAuthority()
    {
        var calls = 0;
        using var model = new ListModel((cursor, _) =>
        {
            calls++;
            return Task.FromResult(Page(cursor is null ? 0 : int.Parse(cursor)));
        });
        var rows = model.Rows;
        await model.StartAsync();
        model.SelectedItem = rows[0];
        await model.ObserveViewportAsync(99, false, false);
        await model.ObserveViewportAsync(199, false, false);
        await model.ObserveViewportAsync(299, false, false);
        Assert.Equal(3, calls);
        Assert.Equal(300, rows.Count);
        Assert.Equal(2, model.AutomaticFillCount);
        Assert.True(model.ContinueCommand.CanExecute(null));
        await model.ContinueCommand.ExecuteAsync(null);
        Assert.Equal(400, rows.Count);
        Assert.Equal("0", model.SelectedItem);
        Assert.Same(rows, model.Rows);
        Assert.Equal(2, model.AutomaticFillCount);
        await model.ObserveViewportAsync(378, true, true);
        Assert.Equal(4, calls);
        await model.ObserveViewportAsync(379, true, true);
        Assert.Equal(5, calls);
    }

    [Fact]
    public async Task UnknownProgrammingErrorsPropagateInsteadOfBecomingRetryableFailures()
    {
        var error = new InvalidOperationException("unexpected");
        using var model = new ListModel((_, _) => throw error);
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => model.StartAsync()));
        Assert.Null(model.Failure);
        Assert.False(model.IsLoading);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateSuccessAndFailureCannotFinishTheNewGeneration(bool fail)
    {
        var old = new TaskCompletionSource<CursorPage<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var current = new TaskCompletionSource<CursorPage<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var model = new ListModel((_, _) => ++calls == 1 ? old.Task : current.Task);
        var first = model.StartAsync();
        var second = model.StartAsync();
        if (fail)
            old.SetException(new IOException());
        else
            old.SetResult(Page(900));
        await first;
        Assert.True(model.IsInitialLoading);
        Assert.Null(model.Failure);
        Assert.Empty(model.Rows);
        current.SetResult(Page(0));
        await second;
        Assert.Equal("100", model.NextCursor);
        Assert.Equal(100, model.Rows.Count);
    }

    [Fact]
    public async Task CursorRecoveryIsOncePerSearchAndResetsRowsSelectionAndBudget()
    {
        var calls = new List<string?>();
        using var model = new ListModel((cursor, _) =>
        {
            calls.Add(cursor);
            return cursor is null ? Task.FromResult(Page(0)) : throw new InvalidCursor();
        });
        await model.StartAsync();
        model.SelectedItem = model.Rows[0];
        await model.ObserveViewportAsync(99, false, false);
        Assert.Equal(new string?[] { null, "100", null }, calls);
        Assert.Null(model.SelectedItem);
        Assert.Equal(0, model.AutomaticFillCount);
        Assert.Null(model.Failure);
        await model.ObserveViewportAsync(99, true, true);
        Assert.Equal(4, calls.Count);
        Assert.IsType<InvalidCursor>(model.Failure);
        Assert.Empty(model.Rows);
        Assert.False(model.HasLoaded);
        await model.StartAsync();
        await model.ObserveViewportAsync(99, true, true);
        Assert.Equal(7, calls.Count);
        Assert.Null(model.Failure);
    }

    [Fact]
    public async Task SuspensionCancelsAndResumesOnePendingCursorWhileFailureRequiresExplicitRetry()
    {
        var pending = new TaskCompletionSource<CursorPage<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken oldToken = default;
        var calls = new List<string?>();
        using var model = new ListModel((cursor, token) =>
        {
            calls.Add(cursor);
            if (calls.Count == 2)
            {
                oldToken = token;
                return pending.Task;
            }
            return calls.Count == 3 ? throw new IOException() : Task.FromResult(Page(cursor is null ? 0 : int.Parse(cursor)));
        });
        await model.StartAsync();
        model.SelectedItem = model.Rows[0];
        var read = model.ObserveViewportAsync(99, false, false);
        model.Suspend();
        Assert.True(oldToken.IsCancellationRequested);
        Assert.False(model.IsLoading);
        Assert.Equal("100", model.NextCursor);
        await model.ResumeAsync();
        pending.SetResult(Page(900));
        await read;
        Assert.True(model.IsContinuationFailure);
        Assert.Equal("0", model.SelectedItem);
        Assert.Equal(100, model.Rows.Count);
        model.Suspend();
        await model.ResumeAsync();
        Assert.Equal(3, calls.Count);
        Assert.True(model.RetryCommand.CanExecute(null));
        await model.RetryCommand.ExecuteAsync(null);
        Assert.Equal(new string?[] { null, "100", "100", "100" }, calls);
        Assert.False(model.RetryCommand.CanExecute(null));
        Assert.Equal(200, model.Rows.Count);
    }
}
