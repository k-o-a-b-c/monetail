using System.Globalization;
using System.Text.Json.Nodes;
using Xunit;

namespace Monetail.Api.Tests;

public sealed partial class ReceiptApiTests
{
    [Fact]
    public async Task TransferListEmptyHasOnlyTwoFieldsAndDoesNotWrite()
    {
        await using var f = await Fixture.Create(); var before = f.Snapshot(); var page = await TransferPage(f, "month=all");
        Keys(page, "transfers", "nextCursor"); Assert.Empty(page["transfers"]!.AsArray()); Assert.Null(page["nextCursor"]); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferListSevenFieldsPreserveValuesAndDoNotExposeRevisionOrAudit()
    {
        await using var f = await Fixture.Create(); var transfer = await SeedTransferDetail(f); var before = f.Snapshot();
        var page = await TransferPage(f, "month=2026-11"); Keys(page, "transfers", "nextCursor"); var row = Assert.Single(page["transfers"]!.AsArray())!.AsObject();
        Keys(row, "transferId", "transactionAt", "name", "fromPaymentMethodId", "toPaymentMethodId", "amount", "memo");
        Assert.Equal(D(transfer.Id), row["transferId"]!.GetValue<string>()); Assert.Equal("2026-11-10T12:34:56", row["transactionAt"]!.GetValue<string>());
        Assert.Equal(transfer.Name, row["name"]!.GetValue<string>()); Assert.Equal(D(f.Payment), row["fromPaymentMethodId"]!.GetValue<string>());
        Assert.Equal(D(f.OtherPayment), row["toPaymentMethodId"]!.GetValue<string>()); Assert.Equal(int.MaxValue, row["amount"]!.GetValue<int>());
        Assert.Null(row["memo"]); Assert.Null(page["nextCursor"]); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(99)] [InlineData(100)] [InlineData(101)] [InlineData(200)] [InlineData(201)]
    public async Task TransferListPageBoundariesHaveStableBinaryOrderWithoutDuplicatesOrMissingRows(int count)
    {
        await using var f = await Fixture.Create(); var ids = SeedTransferList(f, count); var found = new List<string>(); string? cursor = null; var before = f.Snapshot();
        do
        {
            var page = await TransferPage(f, "month=all" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)));
            var rows = page["transfers"]!.AsArray(); Assert.Equal(Math.Min(100, count - found.Count), rows.Count);
            found.AddRange(rows.Select(row => row!["transferId"]!.GetValue<string>())); cursor = page["nextCursor"]?.GetValue<string>();
            Assert.Equal(found.Count < count, cursor is not null);
        } while (cursor is not null);
        Assert.Equal(ids.Reverse().Select(D), found); Assert.Equal(count, found.Distinct().Count()); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferListPagesAcrossMonthsAndUsesInclusiveCalendarBoundaries()
    {
        await using var f = await Fixture.Create(); var older = SeedTransferList(f, 102, at: "2026-11-01T00:00:00");
        var end = SeedTransferList(f, 1, at: "2026-11-30T23:59:59", start: 1000);
        var newer = SeedTransferList(f, 98, at: "2027-01-01T00:00:00", start: 2000);
        SeedTransferList(f, 1, at: "2024-02-29T23:59:59", start: 3000);
        var first = await TransferPage(f, "month=all"); var second = await TransferPage(f, "month=all&cursor=" + Uri.EscapeDataString(first["nextCursor"]!.GetValue<string>()));
        var third = await TransferPage(f, "month=all&cursor=" + Uri.EscapeDataString(second["nextCursor"]!.GetValue<string>()));
        var ids = first["transfers"]!.AsArray().Concat(second["transfers"]!.AsArray()).Concat(third["transfers"]!.AsArray()).Select(row => row!["transferId"]!.GetValue<string>()).ToArray();
        Assert.Equal(newer.Reverse().Concat(end).Concat(older.Reverse()).Append(3001.ToString("x32")).Select(D), ids); Assert.Null(third["nextCursor"]);
        var november = await TransferPage(f, "month=2026-11"); var last = await TransferPage(f, "month=2026-11&cursor=" + Uri.EscapeDataString(november["nextCursor"]!.GetValue<string>()));
        Assert.Equal(103, november["transfers"]!.AsArray().Count + last["transfers"]!.AsArray().Count);
        Assert.Equal(98, (await TransferPage(f, "month=2027-01"))["transfers"]!.AsArray().Count);
        Assert.Single((await TransferPage(f, "month=2024-02"))["transfers"]!.AsArray()); Assert.Empty((await TransferPage(f, "month=2026-12"))["transfers"]!.AsArray());
    }

    [Theory]
    [InlineData("ＡＢＣ１２３", "abc123", true)] [InlineData("abc123", "ＡｂＣ１２３", true)]
    [InlineData("100%_振替", "%_", true)] [InlineData("O'Brien", "'b", true)]
    [InlineData("カナ", "かな", false)] [InlineData("ｶﾅ", "カナ", false)] [InlineData("①", "1", false)]
    [InlineData("ﬁ", "fi", false)] [InlineData("！", "!", false)] [InlineData("Ａ　Ｂ", "a b", false)] [InlineData("Α", "α", false)]
    public async Task TransferListNormalizesOnlyEnglishAlphanumericWidthAndCase(string name, string search, bool match)
    {
        await using var f = await Fixture.Create(); SeedTransferList(f, 1, name: name); var before = f.Snapshot();
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var rows = (await TransferPage(f, "month=all&q=" + Uri.EscapeDataString(search)))["transfers"]!.AsArray(); Assert.Equal(match ? 1 : 0, rows.Count);
            if (match) Assert.Equal(name, rows[0]!["name"]!.GetValue<string>());
        }
        finally { CultureInfo.CurrentCulture = original; }
        Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferListOwnerAndPaymentMatchIndependentSidesAndAllConditionsAreAndedBeforePaging()
    {
        await using var f = await Fixture.Create(); var ids = SeedTransferList(f, 101, name: "ＡＢＣ振替");
        SeedTransferList(f, 2, from: f.OtherPayment, to: f.Payment, name: "ABC振替", start: 1000);
        SeedTransferList(f, 3, name: "別名", start: 2000); SeedTransferList(f, 4, at: "2026-12-01T00:00:00", name: "ABC振替", start: 3000);
        // 所有人物は元、指定支払方法は先でも一致。逆の組合せも同じ結果となる。
        var query = "month=2026-11&ownerPersonId=" + D(f.Owner) + "&paymentMethodId=" + D(f.OtherPayment) + "&q=abc";
        var first = await TransferPage(f, query); var next = await TransferPage(f, query + "&cursor=" + Uri.EscapeDataString(first["nextCursor"]!.GetValue<string>()));
        var expected = new[] { 1002, 1001 }.Select(n => D(n.ToString("x32"))).Concat(ids.Reverse().Select(D)).ToArray();
        Assert.Equal(expected, first["transfers"]!.AsArray().Concat(next["transfers"]!.AsArray()).Select(r => r!["transferId"]!.GetValue<string>()));
        var reverse = await TransferPage(f, "month=2026-11&ownerPersonId=" + D(f.Other) + "&paymentMethodId=" + D(f.Payment) + "&q=abc");
        Assert.Equal(first["transfers"]!.ToJsonString(), reverse["transfers"]!.ToJsonString());
        Assert.Empty((await TransferPage(f, "month=all&ownerPersonId=" + Guid.NewGuid().ToString("D")))["transfers"]!.AsArray());
        Assert.Empty((await TransferPage(f, "month=all&paymentMethodId=" + Guid.NewGuid().ToString("D")))["transfers"]!.AsArray());
        f.App.Database.Execute("UPDATE ledger_people SET valid_to='2025-12-31'; UPDATE ledger_payment_methods SET valid_to='2025-12-31'");
        Assert.Equal(first["transfers"]!.ToJsonString(), (await TransferPage(f, query))["transfers"]!.ToJsonString());
    }

    [Theory]
    [InlineData("")] [InlineData("month=")] [InlineData("month=ALL")] [InlineData("month=2026-1")] [InlineData("month=2026-13")]
    [InlineData("month=all&limit=10")] [InlineData("month=all&month=all")] [InlineData("month=all&q=a&q=b")]
    [InlineData("month=all&ownerPersonId=bad")] [InlineData("month=all&paymentMethodId=")]
    [InlineData("month=all&focusTransferId=00000000-0000-0000-0000-000000000001")]
    public async Task TransferListRejectsInvalidAndUnsupportedQueryFields(string query)
    {
        await using var f = await Fixture.Create(); var before = f.Snapshot(); var error = await TransferPage(f, query, 400);
        Assert.Equal("invalid_input", error["code"]!.GetValue<string>()); Assert.Null(error["latest"]); Assert.Equal(before, f.Snapshot());
        if (query.Contains("focusTransferId", StringComparison.Ordinal)) Assert.Equal("focusTransferId", error["field"]!.GetValue<string>());
    }

    [Fact]
    public async Task TransferListBlankMemoReadsAsNullAndSearchFiltersWholeHistoryWithoutNewLengthLimit()
    {
        await using var f = await Fixture.Create(); SeedTransferList(f, 201, name: "対象外", start: 1000); SeedTransferList(f, 1, name: "一致");
        f.App.Database.Execute("PRAGMA ignore_check_constraints=ON; UPDATE ledger_transfers SET memo=''; PRAGMA ignore_check_constraints=OFF");
        var before = f.Snapshot(); var page = await TransferPage(f, "month=all&q=" + Uri.EscapeDataString("一致"));
        Assert.Null(Assert.Single(page["transfers"]!.AsArray())!["memo"]);
        Assert.Empty((await TransferPage(f, "month=all&q=" + new string('a', 1001)))["transfers"]!.AsArray());
        Assert.Equal(100, (await TransferPage(f, "month=all&q="))["transfers"]!.AsArray().Count); Assert.Equal(before, f.Snapshot());
    }

    private static string TransferRoot(Fixture f) => $"/ledgers/{D(f.Ledger)}/transfers";

    private static Task<JsonObject> TransferPage(Fixture f, string query, int status = 200, HttpClient? client = null) =>
        ReadTransfer(client ?? f.Client, $"/ledgers/{D(f.Ledger)}/transfers?{query}", status);

    private static string[] SeedTransferList(Fixture f, int count, string? from = null, string? to = null,
        string at = "2026-11-10T12:34:56", string name = "振替", int start = 0, string? ledger = null)
    {
        if (count == 0) return [];
        using var connection = f.App.Database.OpenForeignKeyConnection(); using var command = connection.CreateCommand();
        command.CommandText = """
            WITH RECURSIVE numbers(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM numbers WHERE n < @count)
            INSERT INTO ledger_transfers(ledger_id,transfer_id,transaction_at,name,from_payment_method_id,to_payment_method_id,amount,memo,revision,
                created_by_auth_user_id,created_at_utc,updated_by_auth_user_id,updated_at_utc)
            SELECT @ledger,printf('%032x',n+@start),@at,@name,@from,@to,n,NULL,@revision,@user,'2026-11-10 00:00:00',@user,'2026-11-10 00:00:00' FROM numbers
            """;
        foreach (var (key, value) in new (string, object)[] { ("@count", count), ("@start", start), ("@ledger", ledger ?? f.Ledger), ("@at", at),
            ("@name", name), ("@from", from ?? f.Payment), ("@to", to ?? f.OtherPayment), ("@revision", Guid.NewGuid().ToString("N")), ("@user", f.App.UserId) }) command.Parameters.AddWithValue(key, value);
        command.ExecuteNonQuery(); return Enumerable.Range(start + 1, count).Select(n => n.ToString("x32", CultureInfo.InvariantCulture)).ToArray();
    }
}
