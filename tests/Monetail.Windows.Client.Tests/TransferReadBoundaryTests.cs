using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed partial class TransfersWorkspaceTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(100)]
    public async Task TransferListsKeepBoundaryCountsAndApiArrayOrder(int count)
    {
        var rows = Enumerable.Range(0, count).Select(_ => Summary(Guid.NewGuid())).Reverse().ToArray(); using var f = new Fixture(_ => Json(Page(rows)));
        Assert.Equal(rows, (await f.Workspace.ListAsync(new("all"))).Transfers); Assert.Single(f.Requests);
    }

    [Theory]
    [InlineData("list")] [InlineData("detail")] [InlineData("suggestions")] [InlineData("autofill")]
    public async Task TransferMissingAndUnknownPropertiesAreRejectedForEveryRead(string operation)
    {
        var payload = Payload(operation).AsObject(); var property = operation switch { "list" => "nextCursor", "detail" => "revision", "suggestions" => "hasMore", _ => "fromPaymentMethodId" };
        var missing = payload.DeepClone().AsObject(); missing.Remove(property);
        using var absent = new Fixture(_ => Json(missing)); await Assert.ThrowsAsync<ApiConnectionException>(() => Read(absent, operation)); Assert.Single(absent.Requests);
        payload["unknown"] = true; using var extra = new Fixture(_ => Json(payload)); await Assert.ThrowsAsync<ApiConnectionException>(() => Read(extra, operation)); Assert.Single(extra.Requests);
    }

    [Theory]
    [InlineData("list", "nullRows")] [InlineData("list", "nullRow")] [InlineData("list", "duplicateId")] [InlineData("list", "nextType")] [InlineData("list", "count")]
    [InlineData("detail", "wrongId")] [InlineData("detail", "revisionType")] [InlineData("detail", "revisionEmpty")]
    [InlineData("suggestions", "nullName")] [InlineData("suggestions", "emptyName")] [InlineData("suggestions", "duplicateName")] [InlineData("suggestions", "hasMoreType")] [InlineData("suggestions", "count")] [InlineData("suggestions", "longName")]
    [InlineData("autofill", "partialNull")] [InlineData("autofill", "sameId")] [InlineData("autofill", "emptyId")] [InlineData("autofill", "idType")]
    public async Task TransferMalformedShapesDoNotBecomeUsableModels(string operation, string error)
    {
        var payload = Payload(operation);
        if (operation == "list") switch (error) {
            case "nullRows": payload["transfers"] = null; break;
            case "nullRow": payload["transfers"]![0] = null; break;
            case "duplicateId": payload["transfers"]!.AsArray().Add(payload["transfers"]![0]!.DeepClone()); break;
            case "nextType": payload["nextCursor"] = 1; break;
            default: payload = JsonSerializer.SerializeToNode(Page(Enumerable.Range(0, 101).Select(_ => Summary(Guid.NewGuid())).ToArray()), Web)!; break;
        }
        else if (operation == "detail") switch (error) {
            case "wrongId": payload["transferId"] = Guid.NewGuid(); break;
            case "revisionType": payload["revision"] = 1; break;
            default: payload["revision"] = Guid.Empty; break;
        }
        else if (operation == "suggestions") switch (error) {
            case "nullName": payload["suggestions"]![0] = null; break;
            case "emptyName": payload["suggestions"]![0] = ""; break;
            case "duplicateName": payload["suggestions"]!.AsArray().Add("振替"); break;
            case "hasMoreType": payload["hasMore"] = "false"; break;
            case "longName": payload["suggestions"]![0] = new string('名', 101); break;
            default: payload["suggestions"] = new JsonArray(Enumerable.Range(0, 101).Select(i => (JsonNode?)JsonValue.Create(i.ToString())).ToArray()); break;
        }
        else switch (error) {
            case "partialNull": payload["toPaymentMethodId"] = null; break;
            case "sameId": payload["toPaymentMethodId"] = From; break;
            case "emptyId": payload["fromPaymentMethodId"] = Guid.Empty; break;
            default: payload["fromPaymentMethodId"] = 1; break;
        }
        using var f = new Fixture(_ => Json(payload)); await Assert.ThrowsAsync<ApiConnectionException>(() => Read(f, operation)); Assert.Single(f.Requests); Assert.NotNull(f.Store.Current);
    }

    [Theory]
    [InlineData("list")] [InlineData("detail")]
    public async Task TransferRowValidatesEveryRequiredFieldAndIntegerDateUuidTextContracts(string operation)
    {
        var fields = new[] { "transferId", "transactionAt", "name", "fromPaymentMethodId", "toPaymentMethodId", "amount", "memo" };
        foreach (var field in fields)
        {
            var payload = Payload(operation); var row = operation == "list" ? payload["transfers"]![0]!.AsObject() : payload.AsObject(); row.Remove(field);
            using var f = new Fixture(_ => Json(payload)); await Assert.ThrowsAsync<ApiConnectionException>(() => Read(f, operation)); Assert.Single(f.Requests);
        }
    }

    [Theory]
    [InlineData("amount", "0")] [InlineData("amount", "-1")] [InlineData("amount", "2147483648")] [InlineData("amount", "1.0")] [InlineData("amount", "1e1")] [InlineData("amount", "\"1\"")]
    [InlineData("name", "null")] [InlineData("name", "1")] [InlineData("name", "\"　 \"")]
    [InlineData("transactionAt", "\"2026-11-10T23:59:59Z\"")] [InlineData("transactionAt", "\"2026-11-10T23:59:59+09:00\"")]
    [InlineData("transactionAt", "\"2026-11-10T23:59:59.1\"")] [InlineData("transactionAt", "\"2026-02-29T00:00:00\"")]
    [InlineData("transactionAt", "\"2026-11-10 23:59:59\"")] [InlineData("transferId", "\"00000000000040008000000000000001\"")]
    [InlineData("transferId", "\" 00000000-0000-4000-8000-000000000001 \"")]
    [InlineData("memo", "1")] [InlineData("memo", "\"a\\nb\"")]
    public async Task TransferInvalidRowWireValuesAreRejectedForListAndDetail(string field, string raw)
    {
        foreach (var operation in new[] { "list", "detail" })
        {
            var payload = Payload(operation); var row = operation == "list" ? payload["transfers"]![0]! : payload; row[field] = JsonNode.Parse(raw);
            using var f = new Fixture(_ => Json(payload)); await Assert.ThrowsAsync<ApiConnectionException>(() => Read(f, operation)); Assert.Single(f.Requests);
        }
    }

    [Theory]
    [InlineData("list")] [InlineData("detail")] [InlineData("suggestions")] [InlineData("autofill")]
    public async Task TransferMalformedJsonAndDuplicatePropertiesAreRejectedWithoutRetry(string operation)
    {
        var valid = Payload(operation); var key = operation switch { "list" => "nextCursor", "detail" => "name", "suggestions" => "hasMore", _ => "toPaymentMethodId" };
        var repeated = JsonSerializer.Serialize(new Dictionary<string, JsonNode?> { [key] = valid[key]?.DeepClone() }, Web);
        var duplicate = valid.ToJsonString()[..^1] + "," + repeated[1..];
        foreach (var text in new[] { "{", "[]", "null", valid.ToJsonString() + "{}", duplicate })
        {
            using var f = new Fixture(_ => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") });
            await Assert.ThrowsAsync<ApiConnectionException>(() => Read(f, operation)); Assert.Single(f.Requests);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task TransferFocusAndAutofillResponsesMustMatchRequestedTarget(bool focus)
    {
        using var f = new Fixture(_ => focus ? Json(Page([])) : Json(new { fromPaymentMethodId = To, toPaymentMethodId = From }));
        await Assert.ThrowsAsync<ApiConnectionException>(() => focus ? (Task)f.Workspace.ListAsync(new("all", focusTransferId: Transfer)) : f.Workspace.AutofillAsync(new("振替", new(2026, 11, 10), From))); Assert.Single(f.Requests);
        if (focus) { using var wrong = new Fixture(_ => Json(Page([Summary(Guid.NewGuid())]))); await Assert.ThrowsAsync<ApiConnectionException>(() => wrong.Workspace.ListAsync(new("all", focusTransferId: Transfer))); }
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData(" ")]
    public async Task TransferMemoNormalizesOnlyEmptyStringAndDoesNotModifyRawName(string? memo)
    {
        using var f = new Fixture(_ => Json(Page([Summary() with { Memo = memo }]))); var result = Assert.Single((await f.Workspace.ListAsync(new("all"))).Transfers);
        Assert.Equal(memo == "" ? null : memo, result.Memo); Assert.Equal(Summary().Name, result.Name);
    }

    [Theory]
    [InlineData(1)] [InlineData(int.MaxValue)]
    public async Task TransferPositiveInt32EndpointsRemainExact(int amount)
    {
        using var f = new Fixture(_ => Json(Page([Summary() with { Amount = amount }]))); Assert.Equal(amount, Assert.Single((await f.Workspace.ListAsync(new("all"))).Transfers).Amount);
    }

    [Theory]
    [InlineData(50, true)] [InlineData(51, false)]
    public async Task TransferNameAndMemoUseUtf16LengthsWithoutTruncating(int count, bool valid)
    {
        var text = string.Concat(Enumerable.Repeat("😀", count));
        foreach (var field in new[] { "name", "memo" })
        {
            var payload = Payload("detail"); payload[field] = text; using var f = new Fixture(_ => Json(payload));
            if (valid) { var row = await f.Workspace.GetAsync(Transfer); Assert.Equal(text, field == "name" ? row.Name : row.Memo); }
            else await Assert.ThrowsAsync<ApiConnectionException>(() => f.Workspace.GetAsync(Transfer)); Assert.Single(f.Requests);
        }
    }

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static JsonNode Payload(string operation) => JsonSerializer.SerializeToNode(operation switch {
        "list" => Page(), "detail" => Detail(), "suggestions" => new { suggestions = new[] { "振替" }, hasMore = false },
        _ => new { fromPaymentMethodId = (Guid?)From, toPaymentMethodId = (Guid?)To }
    }, Web)!;
    private static Task Read(Fixture f, string operation, CancellationToken token = default) => operation switch {
        "list" => f.Workspace.ListAsync(new("all", cursor: "opaque"), token), "detail" => f.Workspace.GetAsync(Transfer, token),
        "suggestions" => f.Workspace.SuggestAsync(new("ＡＢＣ"), token), _ => f.Workspace.AutofillAsync(new("振替", new(2026, 11, 10)), token)
    };
}
