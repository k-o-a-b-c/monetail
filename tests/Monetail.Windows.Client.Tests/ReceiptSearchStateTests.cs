using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed class ReceiptSearchStateTests
{
    [Fact]
    public void PendingSearchTextIsNotAppliedBySelectionAndInvalidMonthIsAtomic()
    {
        var state = new ReceiptSearchState(new(2026, 12, 31));
        state.MonthText = "2027-02";
        state.ShopText = "未確定";
        state.OwnerPersonId = Guid.NewGuid();
        Assert.Equal("2026-12", state.Query().Month);
        Assert.Equal("", state.Query().Q);
        Assert.Equal("未確定", state.ShopText);
        Assert.True(state.ApplyText());
        Assert.Equal("2027-02", state.Query().Month);
        Assert.Equal("未確定", state.Query().Q);
        state.MonthText = "bad";
        state.ShopText = "変更中";
        Assert.False(state.ApplyText());
        Assert.NotNull(state.MonthError);
        Assert.Equal("未確定", state.Query().Q);
        state.AllPeriods = true;
        Assert.Equal("all", state.Query().Month);
    }

    [Fact]
    public void AllPeriodsAppliesShopWithInvalidPendingMonthWithoutChangingSavedMonth()
    {
        var state = new ReceiptSearchState(new(2026, 11, 13));
        state.MonthText = "bad";
        state.ShopText = "店A";
        Assert.False(state.ApplyText());
        Assert.NotNull(state.MonthError);
        state.AllPeriods = true;
        Assert.Null(state.MonthError);
        state.ShopText = "店B";
        Assert.True(state.ApplyText());
        Assert.Null(state.MonthError);
        Assert.Equal("2026-11", state.AppliedMonth);
        Assert.Equal("all", state.Query().Month);
        Assert.Equal("店B", state.Query().Q);

        state.AllPeriods = false;
        state.ShopText = "店C";
        Assert.False(state.ApplyText());
        Assert.Equal("2026-11", state.Query().Month);
        Assert.Equal("店B", state.Query().Q);
        Assert.NotNull(state.MonthError);

        state.MonthText = "2026-12";
        Assert.True(state.ApplyText());
        Assert.Equal("2026-12", state.Query().Month);
        Assert.Equal("店C", state.Query().Q);
    }

    [Theory]
    [InlineData("2026-12", 1, "2027-01")]
    [InlineData("2027-01", -1, "2026-12")]
    [InlineData("2024-02", 1, "2024-03")]
    public void MonthNavigationUsesCommittedMonthAndPreservesBothDrafts(string month, int delta, string result)
    {
        var state = new ReceiptSearchState(new(2026, 11, 10)) { MonthText = month, ShopText = "確定" };
        Assert.True(state.ApplyText());
        state.MonthText = "編集中";
        state.ShopText = "未確定";
        Assert.True(state.MoveMonth(delta));
        Assert.Equal(result, state.Query().Month);
        Assert.Equal("編集中", state.MonthText);
        Assert.Equal("未確定", state.ShopText);
        Assert.Equal("確定", state.Query().Q);
        state.AllPeriods = true;
        state.PaymentMethodId = Guid.NewGuid();
        Assert.Equal("all", state.Query().Month);
        Assert.Equal("確定", state.Query().Q);
        Assert.False(state.CanMove(delta));
    }
    [Theory]
    [InlineData("0001-01", -1)]
    [InlineData("9999-12", 1)]
    public void MonthNavigationDoesNotOverflow(string month, int delta)
    {
        var state = new ReceiptSearchState(new(2026, 11, 10)) { MonthText = month };
        Assert.True(state.ApplyText());
        Assert.False(state.MoveMonth(delta));
        Assert.Equal(month, state.Query().Month);
    }
}
