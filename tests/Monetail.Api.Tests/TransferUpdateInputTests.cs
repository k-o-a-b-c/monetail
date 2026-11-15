using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Monetail.Api.Tests;

public sealed partial class ReceiptApiTests
{
    [Theory]
    [InlineData("requestId")] [InlineData("revision")] [InlineData("transactionAt")] [InlineData("name")]
    [InlineData("fromPaymentMethodId")] [InlineData("toPaymentMethodId")] [InlineData("amount")] [InlineData("memo")]
    public async Task TransferUpdateRequiresAllEightProperties(string field)
    {
        await using var f = await Fixture.Create(); var path = await CreateTransfer(f, TransferInput(f)); var input = await TransferUpdateInput(f, path); input.Remove(field); var before = f.Snapshot();
        var error = await TransferUpdateError(f, path, input, 400); Assert.Equal("invalid_input", error["code"]!.GetValue<string>()); Assert.Equal(field, error["field"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("revision", "null")] [InlineData("revision", "123")] [InlineData("revision", "\"bad\"")]
    [InlineData("revision", "\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("revision", "\"00000000-0000-4000-0000-000000000001\"")]
    [InlineData("revision", "\"00000000-0000-7000-8000-000000000001\"")]
    [InlineData("revision", "\"00000000000040008000000000000001\"")]
    [InlineData("requestId", "\"00000000-0000-7000-8000-000000000001\"")]
    [InlineData("amount", "0")] [InlineData("amount", "2147483648")] [InlineData("amount", "1.0")] [InlineData("amount", "\"1\"")]
    [InlineData("transactionAt", "\"2026-11-10T12:34:56Z\"")] [InlineData("transactionAt", "\"2026-02-29T00:00:00\"")]
    [InlineData("name", "\"　 \\t\"")] [InlineData("memo", "\"a\\nb\"")] [InlineData("fromPaymentMethodId", "\"bad\"")]
    public async Task TransferUpdateUsesPostScalarValidationAndV4Revision(string field, string raw)
    {
        await using var f = await Fixture.Create(); var path = await CreateTransfer(f, TransferInput(f)); var input = await TransferUpdateInput(f, path); input[field] = JsonNode.Parse(raw); var before = f.Snapshot();
        var error = await TransferUpdateError(f, path, input, 400); Assert.Equal("invalid_input", error["code"]!.GetValue<string>()); Assert.Equal(field, error["field"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("extra")] [InlineData("duplicate")] [InlineData("array")] [InlineData("malformed")] [InlineData("content-type")]
    public async Task TransferUpdateStrictJsonRejectsUnknownDuplicateAndMalformedBodies(string kind)
    {
        await using var f = await Fixture.Create(); var path = await CreateTransfer(f, TransferInput(f)); var text = (await TransferUpdateInput(f, path)).ToJsonString();
        text = kind switch { "extra" => text[..^1] + ",\"transferId\":null}", "duplicate" => text[..^1] + ",\"revision\":null}", "array" => "[]", "malformed" => "{bad", _ => text };
        using var content = new StringContent(text, Encoding.UTF8, kind == "content-type" ? "text/plain" : "application/json"); var before = f.Snapshot();
        using var response = await f.Client.PutAsync(path, content); Assert.Equal(400, (int)response.StatusCode);
        Assert.Equal("invalid_input", (await response.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("name", 50, true)] [InlineData("name", 51, false)] [InlineData("memo", 50, true)] [InlineData("memo", 51, false)]
    public async Task TransferUpdateCountsUtf16AndPreservesOriginalText(string field, int count, bool valid)
    {
        await using var f = await Fixture.Create(); var path = await CreateTransfer(f, TransferInput(f)); var input = await TransferUpdateInput(f, path); var value = string.Concat(Enumerable.Repeat("😀", count)); input[field] = value; var before = f.Snapshot();
        if (valid) { await UpdateTransfer(f, path, input); Assert.Equal(value, (await ReadTransfer(f.Client, path))[field]!.GetValue<string>()); }
        else { Assert.Equal(field, (await TransferUpdateError(f, path, input, 400))["field"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot()); }
    }

    [Fact]
    public async Task TransferUpdateRejectsSamePaymentAndInvalidUrlIds()
    {
        await using var f = await Fixture.Create(); var path = await CreateTransfer(f, TransferInput(f)); var input = await TransferUpdateInput(f, path); input["toPaymentMethodId"] = input["fromPaymentMethodId"]!.GetValue<string>(); var before = f.Snapshot();
        Assert.Equal("toPaymentMethodId", (await TransferUpdateError(f, path, input, 400))["field"]!.GetValue<string>());
        Assert.Equal("transferId", (await TransferUpdateError(f, TransferRoot(f) + "/bad", input, 400))["field"]!.GetValue<string>());
        Assert.Equal("ledgerId", (await TransferUpdateError(f, "/ledgers/bad/transfers/" + Guid.NewGuid(), input, 400))["field"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }
}
