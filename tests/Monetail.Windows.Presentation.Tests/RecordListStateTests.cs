using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Presentation.Lists;
using Xunit;

namespace Monetail.Windows.Presentation.Tests;

public sealed class RecordListStateTests
{
    [Fact]
    public async Task ReceiptStateIsTheExistingModelAndPublishesItsTransitionsAndCommands()
    {
        var read = new TaskCompletionSource<ReceiptListInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var model = ReceiptListViewModelTests.Create((_, _) => read.Task);
        IRecordListState state = model;
        var changed = new List<string?>();
        state.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        var loading = model.StartAsync(new("all"));
        Assert.True(state.IsInitialLoading);
        Assert.False(state.IsEmpty);
        Assert.Same(model.RetryCommand, state.RetryCommand);
        Assert.Same(model.ContinueCommand, state.ContinueCommand);
        read.SetResult(new([], null));
        await loading;
        Assert.False(state.IsInitialLoading);
        Assert.True(state.IsEmpty);
        Assert.Contains(nameof(state.IsInitialLoading), changed);
        Assert.Contains(nameof(state.IsEmpty), changed);
    }

    [Fact]
    public async Task TransferStateKeepsDomainErrorAndFocusedStateWithoutAProjectionCopy()
    {
        var failure = true;
        var id = TransferListViewModelTests.Rows(0, 1)[0].TransferId;
        using var model = TransferListViewModelTests.Create((_, _) => failure ? throw new HttpRequestException()
            : Task.FromResult(new TransferListInfo(TransferListViewModelTests.Rows(0, 1), null)));
        IRecordListState state = model;
        await model.StartAsync(new("all"));
        Assert.True(state.IsInitialFailure);
        Assert.Equal(model.ErrorText, state.ErrorText);
        Assert.False(state.IsContinuationFailure);
        failure = false;
        await model.ShowSavedAsync(new("all", focusTransferId: id), id);
        Assert.False(state.IsInitialFailure);
        Assert.True(state.IsFocused);
        await model.ReturnToStartAsync();
        Assert.False(state.IsFocused);
    }
}
