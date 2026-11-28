using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Xunit;
using static Monetail.Api.Tests.RecordApiTestData;

namespace Monetail.Api.Tests;

public sealed partial class ReceiptListApiTests
{
    [Fact]
    public async Task ListEmptyHasExactTopLevelAndDoesNotWrite()
    {
        await using var f = await RecordApiFixture.Create(); var before = f.Snapshot();
        var page = await ListPage(f, "month=all"); Keys(page, "receipts", "nextCursor");
        Assert.Empty(page["receipts"]!.AsArray()); Assert.Null(page["nextCursor"]); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task ListSevenFieldsUseSavedAmountWithoutChildrenOrRecalculation()
    {
        await using var f = await RecordApiFixture.Create(); SeedList(f, 1); var before = f.Snapshot();
        var page = await ListPage(f, "month=2026-11"); var row = page["receipts"]![0]!.AsObject();
        Keys(row, "receiptId", "transactionAt", "shopName", "shopLocation", "paymentMethodId", "totalAmount", "memo");
        AssertD(row["receiptId"]!); AssertD(row["paymentMethodId"]!); Assert.Equal(D(f.Payment), row["paymentMethodId"]!.GetValue<string>());
        Assert.Equal("2026-11-10T12:34:56", row["transactionAt"]!.GetValue<string>()); Assert.Equal(-100, row["totalAmount"]!.GetValue<int>());
        Assert.Null(row["shopLocation"]); Assert.Null(row["memo"]); Assert.Null(page["nextCursor"]); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(99)] [InlineData(100)] [InlineData(101)] [InlineData(200)] [InlineData(201)]
    public async Task ListPageBoundariesAndEqualTimeBinaryIdOrder(int count)
    {
        await using var f = await RecordApiFixture.Create(); var ids = SeedList(f, count); var found = new List<string>(); string? cursor = null;
        do
        {
            var page = await ListPage(f, "month=all" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)));
            var rows = page["receipts"]!.AsArray(); Assert.Equal(Math.Min(100, count - found.Count), rows.Count);
            found.AddRange(rows.Select(row => row!["receiptId"]!.GetValue<string>())); cursor = page["nextCursor"]?.GetValue<string>();
            Assert.Equal(found.Count < count, cursor is not null);
        } while (cursor is not null);
        Assert.Equal(ids.Reverse().Select(D), found); Assert.Equal(count, found.Distinct().Count());
    }

    [Fact]
    public async Task ListPagingCrossesDateBoundariesWithoutDuplicatesOrMissingRows()
    {
        await using var f = await RecordApiFixture.Create(); var older = SeedList(f, 102);
        var newer = SeedList(f, 99, at: "2026-12-31T23:59:59", start: 1000);
        var expected = newer.Reverse().Concat(older.Reverse()).Select(D).ToArray(); var actual = new List<string>(); string? cursor = null;
        do
        {
            var page = await ListPage(f, "month=all" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)));
            actual.AddRange(page["receipts"]!.AsArray().Select(r => r!["receiptId"]!.GetValue<string>())); cursor = page["nextCursor"]?.GetValue<string>();
        } while (cursor is not null);
        Assert.Equal(expected, actual);
    }

}
