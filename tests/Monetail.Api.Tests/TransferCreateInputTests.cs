using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Monetail.Api.Tests;

public sealed partial class ReceiptApiTests
{
    [Theory]
    [InlineData("amount", "0")] [InlineData("amount", "-1")] [InlineData("amount", "2147483648")]
    [InlineData("amount", "1.0")] [InlineData("amount", "1e1")] [InlineData("amount", "\"1\"")] [InlineData("amount", "null")] [InlineData("amount", "true")]
    [InlineData("name", "null")] [InlineData("name", "5")] [InlineData("name", "[]")]
    [InlineData("memo", "false")] [InlineData("memo", "{}")]
    [InlineData("transactionAt", "null")] [InlineData("transactionAt", "123")]
    [InlineData("transactionAt", "\"2026-11-10T12:34:56Z\"")] [InlineData("transactionAt", "\"2026-11-10T12:34:56+09:00\"")]
    [InlineData("transactionAt", "\"2026-11-10T12:34:56.001\"")] [InlineData("transactionAt", "\"2026-02-29T00:00:00\"")]
    [InlineData("transactionAt", "\"2026-11-10 12:34:56\"")]
    [InlineData("requestId", "null")] [InlineData("requestId", "\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("requestId", "\"00000000-0000-4000-0000-000000000001\"")] [InlineData("requestId", "\"00000000-0000-7000-8000-000000000001\"")]
    [InlineData("fromPaymentMethodId", "null")] [InlineData("fromPaymentMethodId", "\"bad\"")]
    [InlineData("toPaymentMethodId", "[]")] [InlineData("toPaymentMethodId", "\"00000000-0000-0000-0000-000000000000\"")]
    public async Task TransferCreateRejectsInvalidScalarValuesAndTypes(string field, string raw)
    {
        await using var f = await Fixture.Create(); var input = TransferInput(f); input[field] = JsonNode.Parse(raw); var before = f.Snapshot();
        var error = await TransferCreateError(f, input, 400); Assert.Equal("invalid_input", error["code"]!.GetValue<string>()); Assert.Equal(field, error["field"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("requestId")] [InlineData("transactionAt")] [InlineData("name")] [InlineData("fromPaymentMethodId")] [InlineData("toPaymentMethodId")] [InlineData("amount")] [InlineData("memo")]
    public async Task TransferCreateRequiresEveryDeclaredPropertyIncludingNullableMemo(string field)
    {
        await using var f = await Fixture.Create(); var input = TransferInput(f); input.Remove(field); var before = f.Snapshot();
        var error = await TransferCreateError(f, input, 400); Assert.Equal("invalid_input", error["code"]!.GetValue<string>()); Assert.Equal(field, error["field"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("extra")] [InlineData("duplicate")] [InlineData("array")] [InlineData("malformed")] [InlineData("content-type")]
    public async Task TransferCreateStrictJsonRejectsUnknownDuplicateAndMalformedBodies(string kind)
    {
        await using var f = await Fixture.Create(); var text = TransferInput(f).ToJsonString();
        text = kind switch { "extra" => text[..^1] + ",\"revision\":null}", "duplicate" => text[..^1] + ",\"name\":\"duplicate\"}", "array" => "[]", "malformed" => "{bad", _ => text };
        using var content = new StringContent(text, Encoding.UTF8, kind == "content-type" ? "text/plain" : "application/json"); var before = f.Snapshot();
        using var response = await f.Client.PostAsync(TransferRoot(f), content); Assert.Equal(400, (int)response.StatusCode);
        var error = (await response.Content.ReadFromJsonAsync<JsonObject>())!; Assert.Equal("invalid_input", error["code"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferCreateRejectsSamePaymentOnBothSides()
    {
        await using var f = await Fixture.Create(); var input = TransferInput(f); input["toPaymentMethodId"] = input["fromPaymentMethodId"]!.GetValue<string>(); var before = f.Snapshot();
        Assert.Equal("toPaymentMethodId", (await TransferCreateError(f, input, 400))["field"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }
}
