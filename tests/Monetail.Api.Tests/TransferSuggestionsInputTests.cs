using Xunit;

namespace Monetail.Api.Tests;

public sealed partial class ReceiptApiTests
{
    [Theory]
    [InlineData(100, false, 200)] [InlineData(101, false, 400)] [InlineData(50, true, 200)] [InlineData(51, true, 400)]
    public async Task TransferSuggestionsValidateOneHundredUtf16CodeUnits(int count, bool emoji, int status)
    {
        await using var f = await Fixture.Create(); var search = emoji ? string.Concat(Enumerable.Repeat("😀", count)) : new string('a', count);
        if (status == 200) TransferCandidate(f, 1, search); var before = f.Snapshot(); var result = await TransferSuggestions(f, "q=" + Uri.EscapeDataString(search), status);
        if (status == 200) Assert.Equal(new[] { search }, Names(result));
        else { Assert.Equal("invalid_input", result["code"]!.GetValue<string>()); Assert.Equal("q", result["field"]!.GetValue<string>()); }
        Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("q=a&q=b", "q")] [InlineData("q=&q=", "q")] [InlineData("Q=a", "Q")]
    [InlineData("limit=10", "limit")] [InlineData("cursor=bad", "cursor")] [InlineData("kind=shopName", "kind")]
    [InlineData("month=all", "month")] [InlineData("q[]=a", "q[]")]
    public async Task TransferSuggestionsRejectUnknownDuplicateAndMalformedQueryKeys(string query, string field)
    {
        await using var f = await Fixture.Create(); var before = f.Snapshot(); var error = await TransferSuggestions(f, query, 400);
        Assert.Equal("invalid_input", error["code"]!.GetValue<string>()); Assert.Equal(field, error["field"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }
}
