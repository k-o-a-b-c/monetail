using System.Net;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transfers;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed class TransferPagingTests
{
    [Theory]
    [InlineData("2026-12", 1, "2027-01")] [InlineData("2027-01", -1, "2026-12")] [InlineData("2024-02", 1, "2024-03")]
    public void MonthNavigationSharesCommittedMonthAndPreservesPendingText(string month, int delta, string result)
    {
        var common = new RecordSearchState(new(2026, 11, 10)); var state = common.Receipts;
        state.MonthText = month; state.ShopText = "確定"; common.TransferText = "振替確定";
        Assert.True(common.ApplySharedText(common.TransferText));
        state.MonthText = "編集中";
        state.ShopText = "未確定";
        common.TransferText = "振替未確定";
        Assert.True(state.MoveMonth(delta)); Assert.Equal(result, state.Query().Month); Assert.Equal(result, common.TransferQuery().Month);
        Assert.Equal("編集中", state.MonthText); Assert.Equal("未確定", state.ShopText); Assert.Equal("振替未確定", common.TransferText); Assert.Equal("振替確定", common.TransferQuery().Q);
        state.AllPeriods = true; Assert.Equal("all", common.TransferQuery().Month); Assert.False(state.CanMove(delta));
    }
    [Theory]
    [InlineData("0001-01", -1)] [InlineData("9999-12", 1)]
    public void MonthNavigationDoesNotOverflow(string month, int delta)
    {
        var common = new RecordSearchState(new(2026, 11, 10)); var state = common.Receipts;
        state.MonthText = month; Assert.True(state.ApplyMonthText()); Assert.False(state.MoveMonth(delta)); Assert.Equal(month, common.TransferQuery().Month);
    }

}
