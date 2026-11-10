using System.Text;
using Xunit;

namespace Monetail.Api.Tests;

public sealed partial class ReceiptApiTests
{
    [Theory]
    [InlineData("shopName", 0)] [InlineData("shopName", 100)] [InlineData("shopName", 101)]
    [InlineData("shopLocation", 0)] [InlineData("shopLocation", 100)] [InlineData("shopLocation", 101)]
    [InlineData("itemName", 0)] [InlineData("itemName", 100)] [InlineData("itemName", 101)]
    public async Task HistoryLimitsDistinctCandidatesAfterSearchingAllHistory(string kind, int count)
    {
        await using var f = await Fixture.Create();
        for (var i = 1; i <= count; i++) HistoryCandidate(f, kind, i, "候補" + i.ToString("000"));
        var query = HistoryScope(f, kind); var result = await HistoryGet(f, "suggestions", query);
        Assert.Equal(Math.Min(100, count), Names(result).Length); Assert.Equal(count > 100, result["hasMore"]!.GetValue<bool>());
        var empty = await HistoryGet(f, "suggestions", query + "&q="); Assert.Equal(Names(result), Names(empty));
        if (count > 100)
        {
            Assert.Equal(new[] { "候補101" }, Names(await HistoryGet(f, "suggestions", query + "&q=101")));
            Assert.False((await HistoryGet(f, "suggestions", query + "&q=101"))["hasMore"]!.GetValue<bool>());
        }
    }

    [Theory]
    [InlineData("shopName")] [InlineData("shopLocation")] [InlineData("itemName")]
    public async Task HistoryUsesMaxParentUpdateUtcAndRawUtf8BinaryTieBreakWithoutWriting(string kind)
    {
        await using var f = await Fixture.Create();
        var values = new[] { "Z", "a", "あ", "\ue000", "😀" };
        for (var i = 0; i < values.Length; i++) HistoryCandidate(f, kind, i + 1, values[i]);
        Assert.Equal(values, Names(await HistoryGet(f, "suggestions", HistoryScope(f, kind))));
        HistoryCandidate(f, kind, 10, "a", "2026-11-12 00:00:00");
        f.App.Database.Execute("UPDATE ledger_receipts SET transaction_at='2020-01-01T00:00:00' WHERE receipt_id='0000000000000000000000000000000a'");
        Assert.Equal(new[] { "a", "Z", "あ", "\ue000", "😀" }, Names(await HistoryGet(f, "suggestions", HistoryScope(f, kind))));
        f.App.Database.Execute("UPDATE ledger_receipts SET memo='親のメモだけ変更',updated_at_utc='2026-11-13 00:00:00' WHERE receipt_id='00000000000000000000000000000001'");
        var before = f.Snapshot(); Assert.Equal(values, Names(await HistoryGet(f, "suggestions", HistoryScope(f, kind)))); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("shopName", "ＡＢＣ", "ABC|abc|ＡＢＣ")] [InlineData("shopLocation", "abc", "ABC|abc|ＡＢＣ")] [InlineData("itemName", "ＡＢＣ", "ABC|abc|ＡＢＣ")]
    [InlineData("shopName", "か", "かな|カナ|ｶﾅ")] [InlineData("shopLocation", "ｶ", "かな|カナ|ｶﾅ")] [InlineData("itemName", "カ", "かな|カナ|ｶﾅ")]
    [InlineData("shopName", "は", "は")] [InlineData("shopLocation", "は", "は")] [InlineData("itemName", "は", "は")]
    public async Task HistoryNormalizesSearchOnlyAndPreservesOriginalDuplicateIdentity(string kind, string search, string expected)
    {
        await using var f = await Fixture.Create(); var values = new[] { "ABC", "ＡＢＣ", "abc", "かな", "カナ", "ｶﾅ", "は", "ば", "A B", "ABC" };
        for (var i = 0; i < values.Length; i++) HistoryCandidate(f, kind, i + 1, values[i]);
        Assert.Equal(expected.Split('|'), Names(await HistoryGet(f, "suggestions", HistoryScope(f, kind) + "&q=" + Uri.EscapeDataString(search))));
    }

    [Theory]
    [InlineData("shopName")] [InlineData("shopLocation")] [InlineData("itemName")]
    public async Task HistoryDoesNotAdoptBlankCandidateExclusionOrTrim(string kind)
    {
        await using var f = await Fixture.Create(); var values = new[] { "", "\t", " " };
        for (var i = 0; i < values.Length; i++) HistoryCandidate(f, kind, i + 1, values[i]);
        if (kind == "shopLocation") HistoryReceipt(f, 10, "店舗"); // NULLは文字列候補ではない。
        Assert.Equal(values, Names(await HistoryGet(f, "suggestions", HistoryScope(f, kind))));
        Assert.Equal(new[] { " " }, Names(await HistoryGet(f, "suggestions", HistoryScope(f, kind) + "&q=%20")));
    }

    [Theory]
    [InlineData("shopLocation")] [InlineData("itemName")]
    public async Task HistoryExactScopeDoesNotBroadenToNormalizedOrSubstringShop(string kind)
    {
        await using var f = await Fixture.Create();
        var stores = new[] { "ABC", "abc", "ＡＢＣ", "ABC支店", " ABC" };
        for (var i = 0; i < stores.Length; i++)
        {
            var id = HistoryReceipt(f, i + 1, stores[i], location: "所在地" + i); HistoryDetail(f, id, f.Category, "品名" + i);
        }
        var query = "kind=" + kind + "&shopName=ABC" + (kind == "itemName" ? "&categoryId=" + D(f.Category) : "");
        Assert.Equal(new[] { kind == "itemName" ? "品名0" : "所在地0" }, Names(await HistoryGet(f, "suggestions", query)));
    }

    private static void HistoryCandidate(Fixture f, string kind, int number, string candidate, string updated = "2026-11-10 12:00:00")
    {
        var receipt = HistoryReceipt(f, number, kind == "shopName" ? candidate : "店舗", location: kind == "shopLocation" ? candidate : null, updated: updated);
        if (kind == "itemName") HistoryDetail(f, receipt, f.Category, candidate);
    }
    private static string HistoryScope(Fixture f, string kind) => "kind=" + kind + (kind == "shopName" ? "" : "&shopName=" + Uri.EscapeDataString("店舗")) + (kind == "itemName" ? "&categoryId=" + D(f.Category) : "");
}
