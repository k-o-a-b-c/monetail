using Xunit;

namespace Monetail.Api.Tests;

public sealed partial class ReceiptApiTests
{
    [Theory]
    [InlineData("suggestions", "", "kind")] [InlineData("suggestions", "kind=unknown", "kind")]
    [InlineData("suggestions", "kind=shopName&limit=10", "limit")] [InlineData("suggestions", "kind=shopName&q=a&q=b", "q")]
    [InlineData("suggestions", "kind=shopLocation", "shopName")] [InlineData("suggestions", "kind=shopLocation&shopName=", "shopName")]
    [InlineData("suggestions", "kind=itemName&shopName=ABC", "categoryId")] [InlineData("suggestions", "kind=itemName&shopName=ABC&categoryId=bad", "categoryId")]
    [InlineData("suggestions", "kind=shopName&categoryId=bad", "categoryId")]
    [InlineData("autofill", "transactionDate=2026-11-10", "shopName")] [InlineData("autofill", "shopName=ABC", "transactionDate")]
    [InlineData("autofill", "shopName=ABC&transactionDate=2026-02-29", "transactionDate")]
    [InlineData("autofill", "shopName=ABC&transactionDate=2026-11-10T12:00:00", "transactionDate")]
    [InlineData("autofill", "shopName=ABC&transactionDate=2026-11-10&personId=bad", "personId")]
    [InlineData("autofill", "shopName=A&shopName=B&transactionDate=2026-11-10", "shopName")]
    public async Task HistoryRejectsInvalidAndUnsupportedQueriesWithCommonField(string operation, string query, string field)
    {
        await using var f = await Fixture.Create(); var error = await HistoryGet(f, operation, query, 400);
        Assert.Equal("invalid_input", error["code"]!.GetValue<string>()); Assert.Equal(field, error["field"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("q", 100, true)] [InlineData("q", 101, false)] [InlineData("shopName", 100, true)] [InlineData("shopName", 101, false)]
    public async Task HistoryInputLengthCountsOriginalUtf16Units(string field, int length, bool valid)
    {
        await using var f = await Fixture.Create(); var value = string.Concat(Enumerable.Repeat("😀", length / 2)) + (length % 2 == 0 ? "" : "a");
        var query = field == "q" ? "kind=shopName&q=" + Uri.EscapeDataString(value) : "kind=shopLocation&shopName=" + Uri.EscapeDataString(value);
        var result = await HistoryGet(f, "suggestions", query, valid ? 200 : 400);
        if (!valid) Assert.Equal(field, result["field"]!.GetValue<string>());
        else Assert.Empty(Names(result));
        if (field == "shopName") await HistoryGet(f, "autofill", "shopName=" + Uri.EscapeDataString(value) + "&transactionDate=2026-11-10", valid ? 200 : 400);
    }

    [Fact]
    public async Task HistoryDoesNotCountExpandedSearchNormalizationOrAddUrlByteLimits()
    {
        await using var f = await Fixture.Create(); var text = new string('㍿', 100);
        await HistoryGet(f, "suggestions", "kind=shopName&q=" + Uri.EscapeDataString(text));
        await HistoryGet(f, "autofill", "shopName=" + Uri.EscapeDataString(text) + "&transactionDate=2024-02-29");
    }
}
