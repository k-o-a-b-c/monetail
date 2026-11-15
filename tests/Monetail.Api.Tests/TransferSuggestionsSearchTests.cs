using System.Globalization;
using Xunit;

namespace Monetail.Api.Tests;

public sealed partial class ReceiptApiTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(100)] [InlineData(101)]
    public async Task TransferSuggestionsLimitDistinctNamesAndReportHasMoreOnlyAboveOneHundred(int count)
    {
        await using var f = await Fixture.Create(); if (count > 0) { SeedTransferList(f, count); f.App.Database.Execute("UPDATE ledger_transfers SET name=printf('候補%03d',amount)"); }
        var before = f.Snapshot(); var result = await TransferSuggestions(f); Keys(result, "suggestions", "hasMore");
        Assert.Equal(Enumerable.Range(1, Math.Min(count, 100)).Select(i => "候補" + i.ToString("000", CultureInfo.InvariantCulture)), Names(result)); Assert.Equal(count > 100, result["hasMore"]!.GetValue<bool>());
        var empty = await TransferSuggestions(f, "q="); Assert.Equal(Names(result), Names(empty)); Assert.Equal(result["hasMore"]!.GetValue<bool>(), empty["hasMore"]!.GetValue<bool>());
        if (count > 100) { var found = await TransferSuggestions(f, "q=101"); Assert.Equal(new[] { "候補101" }, Names(found)); Assert.False(found["hasMore"]!.GetValue<bool>()); }
        Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferSuggestionsSearchAndGroupAllRowsBeforeLimitingResults()
    {
        await using var f = await Fixture.Create(); SeedTransferList(f, 201, name: "同名", start: 1000); SeedTransferList(f, 101, start: 2000);
        f.App.Database.Execute("UPDATE ledger_transfers SET updated_at_utc='2026-11-12 00:00:00' WHERE name='同名'; UPDATE ledger_transfers SET name=printf('候補%03d',amount) WHERE transfer_id>='000000000000000000000000000007d1'");
        TransferCandidate(f, 1, "古い一致", "2020-01-01 00:00:00"); var before = f.Snapshot();
        var all = await TransferSuggestions(f); Assert.Equal(100, Names(all).Length); Assert.Equal("同名", Names(all)[0]); Assert.True(all["hasMore"]!.GetValue<bool>());
        var old = await TransferSuggestions(f, "q=" + Uri.EscapeDataString("古い一致")); Assert.Equal(new[] { "古い一致" }, Names(old)); Assert.False(old["hasMore"]!.GetValue<bool>());
        var duplicates = await TransferSuggestions(f, "q=" + Uri.EscapeDataString("同名")); Assert.Equal(new[] { "同名" }, Names(duplicates)); Assert.False(duplicates["hasMore"]!.GetValue<bool>()); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferSuggestionsOrderTiesByOriginalUtf8BinaryAndUseUpdateInsteadOfTransactionDate()
    {
        await using var f = await Fixture.Create(); var values = new[] { "Z", "a", "あ", "\ue000", "😀" };
        for (var i = 0; i < values.Length; i++) TransferCandidate(f, i + 1, values[i]);
        Assert.Equal(values, Names(await TransferSuggestions(f)));
        var latest = TransferCandidate(f, 10, "a", "2026-11-12 00:00:00"); f.App.Database.Execute($"UPDATE ledger_transfers SET transaction_at='1900-01-01T00:00:00' WHERE transfer_id='{latest}'");
        var before = f.Snapshot(); Assert.Equal(new[] { "a", "Z", "あ", "\ue000", "😀" }, Names(await TransferSuggestions(f))); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("abc1")] [InlineData("ＡｂＣ１")]
    public async Task TransferSuggestionsFoldOnlySearchAlphanumericsAndKeepOriginalSpellingAndIdentity(string search)
    {
        await using var f = await Fixture.Create(); var values = new[] { "ABC1", "abc1", "銀行abc1", "ＡＢＣ１", "ABC1" };
        for (var i = 0; i < values.Length; i++) TransferCandidate(f, i + 1, values[i]); var before = f.Snapshot();
        Assert.Equal(values.Take(4), Names(await TransferSuggestions(f, "q=" + Uri.EscapeDataString(search)))); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("かな", "カナ")] [InlineData("ｶﾅ", "カナ")] [InlineData("＋", "+")] [InlineData("①", "1")]
    [InlineData("ﬀ", "ff")] [InlineData("K", "k")] [InlineData("é", "É")] [InlineData("長ー音", "長-音")] [InlineData("全　角", "全 角")]
    public async Task TransferSuggestionsDoNotNormalizeKanaSymbolsCompatibilityOrOtherLetters(string name, string search)
    {
        await using var f = await Fixture.Create(); TransferCandidate(f, 1, name); var before = f.Snapshot();
        Assert.Empty(Names(await TransferSuggestions(f, "q=" + Uri.EscapeDataString(search)))); Assert.Equal(new[] { name }, Names(await TransferSuggestions(f, "q=" + Uri.EscapeDataString(name)))); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferSuggestionsDoNotTrimOrInterpretSqlWildcardsAndQueryText()
    {
        await using var f = await Fixture.Create(); TransferCandidate(f, 1, " 100%_銀行 "); TransferCandidate(f, 2, "100XX銀行"); var before = f.Snapshot();
        Assert.Equal(new[] { " 100%_銀行 " }, Names(await TransferSuggestions(f, "q=" + Uri.EscapeDataString("%_"))));
        Assert.Equal(new[] { " 100%_銀行 " }, Names(await TransferSuggestions(f, "q=%20")));
        Assert.Empty(Names(await TransferSuggestions(f, "q=" + Uri.EscapeDataString("' OR 1=1--")))); Assert.Equal(before, f.Snapshot());
    }
}
