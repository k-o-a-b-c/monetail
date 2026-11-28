using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;
using static Monetail.Api.Tests.RecordApiTestData;

namespace Monetail.Api.Tests;

public sealed partial class TransferDeleteApiTests
{
    [Theory]
    [InlineData("requestId")] [InlineData("revision")]
    public async Task TransferDeleteRequiresBothProperties(string field)
    {
        await using var f = await RecordApiFixture.Create(); var path = await CreateTransfer(f, TransferInput(f)); var input = await TransferDeleteInput(f, path); input.Remove(field); var before = f.Snapshot();
        var error = await TransferDeleteError(f, path, input, 400); Assert.Equal("invalid_input", error["code"]!.GetValue<string>()); Assert.Equal(field, error["field"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("null")] [InlineData("123")] [InlineData("true")] [InlineData("[]")] [InlineData("{}")]
    [InlineData("\"bad\"")] [InlineData("\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("\"00000000-0000-7000-8000-000000000001\"")] [InlineData("\"00000000-0000-4000-0000-000000000001\"")]
    [InlineData("\"00000000000040008000000000000001\"")] [InlineData("\" 00000000-0000-4000-8000-000000000001 \"")]
    public async Task TransferDeleteRejectsNonV4AndInvalidTypesForEachUuid(string raw)
    {
        await using var f = await RecordApiFixture.Create(); var path = await CreateTransfer(f, TransferInput(f)); var valid = await TransferDeleteInput(f, path); var before = f.Snapshot();
        foreach (var field in new[] { "requestId", "revision" })
        {
            var input = valid.DeepClone().AsObject(); input[field] = JsonNode.Parse(raw); var error = await TransferDeleteError(f, path, input, 400);
            Assert.Equal("invalid_input", error["code"]!.GetValue<string>()); Assert.Equal(field, error["field"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
        }
    }

    [Theory]
    [InlineData("extra")] [InlineData("duplicate-request")] [InlineData("duplicate-revision")] [InlineData("array")] [InlineData("malformed")] [InlineData("content-type")]
    public async Task TransferDeleteStrictJsonAcceptsOnlyTwoUniqueProperties(string kind)
    {
        await using var f = await RecordApiFixture.Create(); var path = await CreateTransfer(f, TransferInput(f)); var text = (await TransferDeleteInput(f, path)).ToJsonString();
        text = kind switch { "extra" => text[..^1] + ",\"transferId\":null}", "duplicate-request" => text[..^1] + ",\"requestId\":null}", "duplicate-revision" => text[..^1] + ",\"revision\":null}", "array" => "[]", "malformed" => "{bad", _ => text };
        using var request = new HttpRequestMessage(HttpMethod.Delete, path) { Content = new StringContent(text, Encoding.UTF8, kind == "content-type" ? "text/plain" : "application/json") }; var before = f.Snapshot();
        using var response = await f.Client.SendAsync(request); Assert.Equal(400, (int)response.StatusCode); Assert.Equal("invalid_input", (await response.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("ledger")] [InlineData("transfer")]
    public async Task TransferDeleteRejectsInvalidUrlIds(string kind)
    {
        await using var f = await RecordApiFixture.Create(); var path = await CreateTransfer(f, TransferInput(f)); var input = await TransferDeleteInput(f, path); var before = f.Snapshot();
        path = kind == "ledger" ? "/ledgers/bad/transfers/" + Guid.NewGuid() : TransferRoot(f) + "/bad";
        Assert.Equal(kind + "Id", (await TransferDeleteError(f, path, input, 400))["field"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }
}
