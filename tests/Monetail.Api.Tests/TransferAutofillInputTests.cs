using Xunit;
using static Monetail.Api.Tests.RecordApiTestData;

namespace Monetail.Api.Tests;

public sealed partial class TransferAutofillApiTests
{
    [Theory]
    [InlineData("transactionDate=2026-11-10", "name")] [InlineData("name=&transactionDate=2026-11-10", "name")]
    [InlineData("name=振替", "transactionDate")] [InlineData("name=振替&transactionDate=", "transactionDate")]
    [InlineData("name=振替&name=重複&transactionDate=2026-11-10", "name")]
    [InlineData("name=振替&transactionDate=2026-11-10&transactionDate=2026-11-10", "transactionDate")]
    [InlineData("name=振替&transactionDate=2026-11-10&fromPaymentMethodId=&fromPaymentMethodId=", "fromPaymentMethodId")]
    [InlineData("Name=振替&transactionDate=2026-11-10", "Name")] [InlineData("name=振替&transactionDate=2026-11-10&q=振替", "q")]
    [InlineData("name=振替&transactionDate=2026-11-10&limit=100", "limit")]
    public async Task TransferAutofillRejectsMissingDuplicateAndUnknownQueryFields(string query, string field)
    {
        await using var f = await RecordApiFixture.Create(); var before = f.Snapshot(); var error = await TransferAutofill(f, query, 400);
        Assert.Equal("invalid_input", error["code"]!.GetValue<string>()); Assert.Equal(field, error["field"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("2026-1-10")] [InlineData("2026-11-1")] [InlineData("2026-02-29")] [InlineData("0000-01-01")]
    [InlineData("2026-11-10T00:00:00")] [InlineData("2026-11-10Z")] [InlineData("2026-11-10+09:00")] [InlineData(" 2026-11-10 ")]
    public async Task TransferAutofillRequiresExactRealCalendarDate(string date)
    {
        await using var f = await RecordApiFixture.Create(); var before = f.Snapshot(); Assert.Equal("transactionDate", (await TransferAutofill(f, TransferAutofillQuery(date: date), 400))["field"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData(100, false, 200)] [InlineData(101, false, 400)] [InlineData(50, true, 200)] [InlineData(51, true, 400)]
    public async Task TransferAutofillCountsNameInUtf16CodeUnitsAndPreservesOriginalText(int count, bool emoji, int status)
    {
        await using var f = await RecordApiFixture.Create(); var name = emoji ? string.Concat(Enumerable.Repeat("😀", count)) : new string('a', count); if (status == 200) TransferCandidate(f, 1, name); var before = f.Snapshot();
        var result = await TransferAutofill(f, TransferAutofillQuery(name), status); if (status == 200) AssertTransferPair(result, f.Payment, f.OtherPayment); else Assert.Equal("name", result["field"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("")] [InlineData("bad")] [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("00000000000040008000000000000001")] [InlineData(" 00000000-0000-4000-8000-000000000001 ")]
    public async Task TransferAutofillRequiresNonemptyDUuidForSpecifiedSource(string source)
    {
        await using var f = await RecordApiFixture.Create(); var before = f.Snapshot(); Assert.Equal("fromPaymentMethodId", (await TransferAutofill(f, TransferAutofillQuery(from: source), 400))["field"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }
}
