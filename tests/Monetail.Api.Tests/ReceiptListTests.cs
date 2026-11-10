using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Monetail.Api.Tests;

public sealed partial class ReceiptApiTests
{
    [Fact]
    public async Task ListEmptyHasExactTopLevelAndDoesNotWrite()
    {
        await using var f = await Fixture.Create(); var before = f.Snapshot();
        var page = await ListPage(f, "month=all"); Keys(page, "receipts", "nextCursor");
        Assert.Empty(page["receipts"]!.AsArray()); Assert.Null(page["nextCursor"]); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task ListSevenFieldsUseSavedAmountWithoutChildrenOrRecalculation()
    {
        await using var f = await Fixture.Create(); SeedList(f, 1); var before = f.Snapshot();
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
        await using var f = await Fixture.Create(); var ids = SeedList(f, count); var found = new List<string>(); string? cursor = null;
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
        await using var f = await Fixture.Create(); var older = SeedList(f, 102);
        var newer = SeedList(f, 99, at: "2026-12-31T23:59:59", start: 1000);
        var expected = newer.Reverse().Concat(older.Reverse()).Select(D).ToArray(); var actual = new List<string>(); string? cursor = null;
        do
        {
            var page = await ListPage(f, "month=all" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)));
            actual.AddRange(page["receipts"]!.AsArray().Select(r => r!["receiptId"]!.GetValue<string>())); cursor = page["nextCursor"]?.GetValue<string>();
        } while (cursor is not null);
        Assert.Equal(expected, actual);
    }

    private static async Task<JsonObject> ListPage(Fixture f, string query, int status = 200, HttpClient? client = null)
    {
        using var response = await (client ?? f.Client).GetAsync(f.Root + "?" + query);
        var text = await response.Content.ReadAsStringAsync(); Assert.True((int)response.StatusCode == status, $"Expected {status}, got {(int)response.StatusCode}: {text}");
        return JsonNode.Parse(text)!.AsObject();
    }
    private static string[] SeedList(Fixture f, int count, string? payment = null, string at = "2026-11-10T12:34:56", string shop = "店舗", int start = 0)
    {
        if (count == 0) return [];
        using var connection = f.App.Database.OpenForeignKeyConnection(); using var command = connection.CreateCommand();
        command.CommandText = """
            WITH RECURSIVE numbers(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM numbers WHERE n < @count)
            INSERT INTO ledger_receipts(ledger_id,receipt_id,transaction_at,shop_name,shop_location,payment_method_id,tax_inclusion_mode,tax_rounding_mode,
              recurrence_interval,recurrence_unit,total_amount,memo,revision,created_by_auth_user_id,created_at_utc,updated_by_auth_user_id,updated_at_utc)
            SELECT @ledger,printf('%032x',n+@start),@at,@shop,NULL,@payment,0,'floor',NULL,NULL,n-101,NULL,@revision,@user,'2026-11-10 00:00:00',@user,'2026-11-10 00:00:00'
            FROM numbers
            """;
        foreach (var (key, value) in new (string, object)[] { ("@count", count), ("@start", start), ("@ledger", f.Ledger), ("@at", at),
            ("@shop", shop), ("@payment", payment ?? f.Payment), ("@revision", Guid.NewGuid().ToString("N")), ("@user", f.App.UserId) }) command.Parameters.AddWithValue(key, value);
        command.ExecuteNonQuery(); return Enumerable.Range(start + 1, count).Select(n => n.ToString("x32", CultureInfo.InvariantCulture)).ToArray();
    }
}
