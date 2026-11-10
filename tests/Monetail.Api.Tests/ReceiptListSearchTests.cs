using System.Globalization;
using System.Text.Json.Nodes;
using Xunit;

namespace Monetail.Api.Tests;

public sealed partial class ReceiptApiTests
{
    [Theory]
    [InlineData("2026-11", 2)] [InlineData("2026-12", 1)] [InlineData("2027-01", 1)]
    [InlineData("2024-02", 1)] [InlineData("all", 7)] [InlineData("0001-01", 1)] [InlineData("9999-12", 1)]
    public async Task ListMonthUsesInclusiveCalendarBoundariesAndAll(string month, int expected)
    {
        await using var f = await Fixture.Create();
        var dates = new[] { "2026-11-01T00:00:00", "2026-11-30T23:59:59", "2026-12-31T23:59:59", "2027-01-01T00:00:00",
            "2024-02-29T23:59:59", "0001-01-01T00:00:00", "9999-12-31T23:59:59" };
        for (var i = 0; i < dates.Length; i++) SeedList(f, 1, at: dates[i], start: i);
        var rows = (await ListPage(f, "month=" + month))["receipts"]!.AsArray(); Assert.Equal(expected, rows.Count);
        Assert.Equal(rows.Select(r => r!["transactionAt"]!.GetValue<string>()).OrderDescending(StringComparer.Ordinal), rows.Select(r => r!["transactionAt"]!.GetValue<string>()));
    }

    [Theory]
    [InlineData("スーパー", "すーぱー")] [InlineData("ｽｰﾊﾟｰ", "スーパー")] [InlineData("すーぱー", "ｽｰﾊﾟｰ")]
    [InlineData("ＡＢＣ本店", "abc")] [InlineData("abc本店", "ＡＢＣ")] [InlineData("ガーデン", "か\u3099ーて\u3099ん")]
    [InlineData("ヴァイオリン", "ゔぁいおりん")] [InlineData("ヷ", "わ\u3099")]
    [InlineData("100%_店", "%_")] [InlineData("O'Brien", "'B")]
    public async Task ListShopSearchNormalizesBothSidesAndUsesLiteralSubstring(string shop, string search)
    {
        await using var f = await Fixture.Create(); SeedList(f, 1, shop: "前" + shop + "後"); SeedList(f, 1, shop: "対象外", start: 1);
        var before = f.Snapshot(); var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var rows = (await ListPage(f, "month=all&q=" + Uri.EscapeDataString(search)))["receipts"]!.AsArray();
            Assert.Single(rows); Assert.Equal("前" + shop + "後", rows[0]!["shopName"]!.GetValue<string>());
        }
        finally { CultureInfo.CurrentCulture = culture; }
        Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("は", "ば")] [InlineData("ABC DEF", "abcdef")] [InlineData("スーパー", "コンビニ")]
    public async Task ListSearchDoesNotRemoveVoicingSpacesOrInventSynonyms(string shop, string search)
    {
        await using var f = await Fixture.Create(); SeedList(f, 1, shop: shop);
        Assert.Empty((await ListPage(f, "month=all&q=" + Uri.EscapeDataString(search)))["receipts"]!.AsArray());
    }

    [Fact]
    public async Task ListPayerPaymentAndShopConditionsAreAndedBeforePaging()
    {
        await using var f = await Fixture.Create();
        var ids = SeedList(f, 201, shop: "スーパー"); SeedList(f, 3, f.OtherPayment, shop: "スーパー", start: 1000);
        SeedList(f, 4, shop: "薬局", start: 2000); SeedList(f, 5, at: "2026-12-01T00:00:00", shop: "スーパー", start: 3000);
        var owner = "&ownerPersonId=" + D(f.Owner); var payment = "&paymentMethodId=" + D(f.Payment);
        var conditions = "month=2026-11" + owner + payment + "&q=" + Uri.EscapeDataString("すーぱー");
        var first = await ListPage(f, conditions); var second = await ListPage(f, conditions + "&cursor=" + Uri.EscapeDataString(first["nextCursor"]!.GetValue<string>()));
        Assert.Equal(ids.Reverse().Take(200).Select(D), first["receipts"]!.AsArray().Concat(second["receipts"]!.AsArray()).Select(r => r!["receiptId"]!.GetValue<string>()));
        Assert.Equal(3, (await ListPage(f, "month=2026-11&ownerPersonId=" + D(f.Other)))["receipts"]!.AsArray().Count);
        Assert.Equal(3, (await ListPage(f, "month=2026-11&paymentMethodId=" + D(f.OtherPayment)))["receipts"]!.AsArray().Count);
        Assert.Empty((await ListPage(f, "month=all" + owner + "&paymentMethodId=" + D(f.OtherPayment)))["receipts"]!.AsArray());
        // 支払者は作成者/明細対象者ではなく支払方法の所有人物。期間外のマスタでも過去の一覧は残す。
        f.App.Database.Execute("UPDATE ledger_people SET valid_to='2025-12-31'; UPDATE ledger_payment_methods SET valid_to='2025-12-31'");
        Assert.Equal(3, (await ListPage(f, "month=all&ownerPersonId=" + D(f.Other)))["receipts"]!.AsArray().Count);
        Assert.Empty((await ListPage(f, "month=all&paymentMethodId=" + Guid.NewGuid().ToString("D")))["receipts"]!.AsArray());
    }

    [Fact]
    public async Task ListSearchFiltersTheWholeResultAndDoesNotAddAnInputLengthLimit()
    {
        await using var f = await Fixture.Create(); SeedList(f, 201, shop: "対象外", start: 1000); SeedList(f, 1, shop: "一致");
        var rows = (await ListPage(f, "month=all&q=" + Uri.EscapeDataString("一致")))["receipts"]!.AsArray(); Assert.Single(rows);
        Assert.Empty((await ListPage(f, "month=all&q=" + new string('a', 1001)))["receipts"]!.AsArray());
        Assert.Equal(100, (await ListPage(f, "month=all&q="))["receipts"]!.AsArray().Count);
    }

    [Theory]
    [InlineData("")] [InlineData("month=")] [InlineData("month=ALL")] [InlineData("month=2026-1")] [InlineData("month=0000-01")]
    [InlineData("month=2026-13")] [InlineData("month=2026-02-01")] [InlineData("month=all&limit=10")] [InlineData("month=all&month=all")]
    [InlineData("month=all&q=x&q=y")] [InlineData("month=all&ownerPersonId=bad")] [InlineData("month=all&paymentMethodId=")]
    public async Task ListRejectsMalformedOrAdditionalQueryInputs(string query)
    {
        await using var f = await Fixture.Create();
        Assert.Equal("invalid_input", (await ListPage(f, query, 400))["code"]!.GetValue<string>());
    }
}
