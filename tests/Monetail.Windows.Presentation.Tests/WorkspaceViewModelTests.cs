using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Presentation.Lists;
using Monetail.Windows.Presentation.Receipts;
using Monetail.Windows.Presentation.Transfers;
using Monetail.Windows.Presentation.Workspace;
using Xunit;

namespace Monetail.Windows.Presentation.Tests;
public sealed class WorkspaceViewModelTests
{
    [Fact]
    public async Task SharedCommandsUseOneSearchStateAndDoNotApplyDraftsOnSelection()
    {
        var search = new RecordSearchState(new(2026, 12, 31));
        var receiptQueries = new List<ReceiptListQuery>();
        var transferQueries = new List<TransferListQuery>();
        using var model = new RecordHomeViewModel(search, new((q, _) =>
        {
            receiptQueries.Add(q);
            return Task.FromResult(new ReceiptListInfo([], null));
        }, search, _ => "現金"), new((q, _) =>
        {
            transferQueries.Add(q);
            return Task.FromResult(new TransferListInfo([], null));
        }, search, _ => "現金"));
        await model.StartAsync();
        model.MonthText = "2027-01";
        model.Keyword = "未確定";
        await model.SetOwnerCommand.ExecuteAsync(new RecordFilterChoice(Guid.NewGuid(), "人物"));
        Assert.Equal("2026-12", receiptQueries[^1].Month);
        Assert.Equal("", transferQueries[^1].Q);
        Assert.Equal("2027-01", model.MonthText);
        await model.ApplyKeywordCommand.ExecuteAsync(null);
        Assert.Equal("2027-01", receiptQueries[^1].Month);
        Assert.Equal("未確定", transferQueries[^1].Q);
        await model.MoveMonthCommand.ExecuteAsync(-1);
        Assert.Equal("2026-12", receiptQueries[^1].Month);
        Assert.Same(search, model.Search);
        Assert.Equal(receiptQueries.Count, transferQueries.Count);
    }

    [Fact]
    public async Task InvalidMonthDoesNotFetchAndSuspendPreservesRowsSelectionAndViewport()
    {
        var search = new RecordSearchState(new(2026, 11, 1));
        var count = 0;
        using var model = new RecordHomeViewModel(search, new((_, _) =>
        {
            count++;
            return Task.FromResult(new ReceiptListInfo(ReceiptListViewModelTests.Rows(0, 1), null));
        }, search, _ => "現金"), new((_, _) => Task.FromResult(new TransferListInfo([], null)), search, _ => "現金"));
        await model.StartAsync();
        model.Receipts.SelectedItem = model.Receipts.Rows[0];
        var row = model.Receipts.SelectedItem;
        model.Receipts.ViewPosition = new(12, 40);
        model.MonthText = "不正年月";
        await model.ApplyMonthCommand.ExecuteAsync(null);
        Assert.NotNull(model.MonthError);
        Assert.Equal(1, count);
        model.Suspend();
        await model.ResumeAsync();
        Assert.Equal(1, count);
        Assert.Same(row, model.Receipts.SelectedItem);
        Assert.Equal(new ListViewportPosition(12, 40), model.Receipts.ViewPosition);
        Assert.Equal("不正年月", model.MonthText);
    }

    [Fact]
    public async Task DisposalStopsOldRequestsAndCannotRestartThem()
    {
        var search = new RecordSearchState(new(2026, 11, 1));
        var pending = new TaskCompletionSource<ReceiptListInfo>();
        var requests = 0;
        var model = new RecordHomeViewModel(search, new((_, _) =>
        {
            requests++;
            return pending.Task;
        }, search, _ => "現金"), new((_, _) => Task.FromResult(new TransferListInfo([], null)), search, _ => "現金"));
        var read = model.StartAsync();
        model.Dispose();
        model.Dispose();
        pending.SetResult(new(ReceiptListViewModelTests.Rows(0, 1), null));
        await read;
        await model.ResumeAsync();
        await model.ApplyMonthCommand.ExecuteAsync(null);
        Assert.Empty(model.Receipts.Rows);
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task ManagementCommandRequiresShellApprovalAndHasOneSelection()
    {
        var allowed = true;
        var requests = new List<ManagementSection>();
        using var fixture = new ManagementTestContext(_ => throw new InvalidOperationException());
        using var model = new ManagementViewModel(fixture.Context, () => allowed, (_, _) => Task.CompletedTask, () => Task.CompletedTask, "本人");
        model.NavigationRequested += section =>
        {
            requests.Add(section);
            return Task.CompletedTask;
        };
        model.SelectedSection = ManagementSection.Account;
        Assert.Empty(requests);
        await model.SelectCommand.ExecuteAsync(ManagementSection.People);
        Assert.Equal(new[] { ManagementSection.People }, requests);
        allowed = false;
        await model.SelectCommand.ExecuteAsync(ManagementSection.Logout);
        Assert.Single(requests);
        Assert.Equal(ManagementSection.Account, model.SelectedSection);
    }
}
