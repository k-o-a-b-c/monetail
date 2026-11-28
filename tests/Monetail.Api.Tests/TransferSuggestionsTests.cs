using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;
using static Monetail.Api.Tests.RecordApiTestData;

namespace Monetail.Api.Tests;

public sealed partial class TransferSuggestionsApiTests
{
    [Fact]
    public async Task TransferSuggestionsEmptyHistoryReturnsOnlyEmptyArrayAndFalseWithoutWriting()
    {
        await using var f = await RecordApiFixture.Create(); var before = f.Snapshot(); var result = await TransferSuggestions(f);
        Keys(result, "suggestions", "hasMore"); Assert.Empty(Names(result)); Assert.False(result["hasMore"]!.GetValue<bool>()); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferSuggestionsUseFullHistoryAndMaximumUpdateUtcForEachOriginalName()
    {
        await using var f = await RecordApiFixture.Create(); TransferCandidate(f, 1, "銀行へ入金", "2026-11-01 00:00:00"); TransferCandidate(f, 2, "銀行間振替", "2026-11-02 00:00:00"); TransferCandidate(f, 3, "銀行へ入金", "2026-11-03 00:00:00");
        var before = f.Snapshot(); var result = await TransferSuggestions(f); Keys(result, "suggestions", "hasMore");
        Assert.Equal(new[] { "銀行へ入金", "銀行間振替" }, Names(result)); Assert.False(result["hasMore"]!.GetValue<bool>());
        Assert.Equal(Names(result), Names(await TransferSuggestions(f, "q="))); Assert.Equal(new[] { "銀行へ入金" }, Names(await TransferSuggestions(f, "q=" + Uri.EscapeDataString("へ入"))));
        Assert.Empty(Names(await TransferSuggestions(f, "q=" + Uri.EscapeDataString("存在しない")))); Assert.Equal(before, f.Snapshot());
    }

}
