using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed partial class ReceiptsWorkspaceTests
{
    // LedgerApiFault.Bodyのreasonなし分岐と同じ5項目。nullable項目も省略しない。
    private static JsonObject LedgerConflictBody(string code, JsonNode? latest, bool withMetadata = false) => new()
    {
        ["code"] = code, ["message"] = "非公開メッセージ・SQL・内部接続情報",
        ["field"] = withMetadata ? "revision" : null, ["latest"] = latest,
        ["permissions"] = withMetadata ? JsonSerializer.SerializeToNode(new { currentRole = "Admin", myPersonId = Owner.ToString("N") }) : null
    };

    [Theory]
    [InlineData(ReceiptWriteOperation.Update, false)] [InlineData(ReceiptWriteOperation.Delete, false)]
    [InlineData(ReceiptWriteOperation.Update, true)] [InlineData(ReceiptWriteOperation.Delete, true)]
    public async Task ActualRevisionConflictEnvelopeKeepsValidatedLatestButDoesNotExposeServerMessage(ReceiptWriteOperation operation, bool metadata)
    {
        var payload = LedgerConflictBody("revision_conflict", JsonSerializer.SerializeToNode(Detail()), metadata);
        Assert.Equal(new[] { "code", "message", "field", "latest", "permissions" }, payload.Select(p => p.Key));
        if (!metadata) { Assert.Null(payload["field"]); Assert.Null(payload["permissions"]); }
        using var f = new WriteFixture((_, _) => Task.FromResult(Json(payload, HttpStatusCode.Conflict)));
        var error = await Assert.ThrowsAsync<ReceiptRequestException>(() => Write(f, WriteRequest(operation)));
        Assert.Equal(HttpStatusCode.Conflict, error.StatusCode); Assert.Equal("revision_conflict", error.Code);
        Assert.Equal(metadata ? "revision" : null, error.Field); Assert.Null(error.Permissions);
        Assert.Equal(Receipt, error.LatestReceipt!.ReceiptId); Assert.Equal(2, Assert.Single(error.LatestReceipt.Groups).Details.Length);
        Assert.Null(error.LatestReceipt.Groups[0].Details[0].TaxRate); Assert.Equal(0m, error.LatestReceipt.Groups[0].Details[1].TaxRate);
        Assert.DoesNotContain("非公開", error.ToString()); Assert.DoesNotContain("SQL", error.ToString()); Assert.Single(f.Requests);
    }

    [Theory]
    [InlineData(ReceiptWriteOperation.Create)] [InlineData(ReceiptWriteOperation.Update)] [InlineData(ReceiptWriteOperation.Delete)]
    public async Task ActualRequestConflictEnvelopeWithNullFieldsIsAConfirmedBusinessError(ReceiptWriteOperation operation)
    {
        var payload = LedgerConflictBody("request_conflict", null);
        Assert.Equal(5, payload.Count); Assert.Null(payload["field"]); Assert.Null(payload["latest"]); Assert.Null(payload["permissions"]);
        using var f = new WriteFixture((_, _) => Task.FromResult(Json(payload, HttpStatusCode.Conflict)));
        var request = WriteRequest(operation);
        var error = await Assert.ThrowsAsync<ReceiptRequestException>(() => Write(f, request));
        Assert.Equal(HttpStatusCode.Conflict, error.StatusCode); Assert.Equal("request_conflict", error.Code);
        Assert.Null(error.Field); Assert.Null(error.LatestReceipt); Assert.Null(error.Permissions);
        Assert.DoesNotContain("内部接続情報", error.ToString()); Assert.Single(f.Requests); Assert.NotNull(f.Store.Current);
    }

    [Fact]
    public async Task AcceptingCommonEnvelopeDoesNotAcceptUnknownPrivateProperties()
    {
        var payload = LedgerConflictBody("revision_conflict", JsonSerializer.SerializeToNode(Detail())); payload["internalSql"] = "非公開";
        using var f = new WriteFixture((_, _) => Task.FromResult(Json(payload, HttpStatusCode.Conflict)));
        var error = await Assert.ThrowsAsync<ReceiptWriteUncertainException>(() => Write(f, WriteRequest(ReceiptWriteOperation.Update)));
        Assert.Equal(ReceiptWriteFailureKind.InvalidResponse, error.Kind); Assert.DoesNotContain("非公開", error.ToString());
    }
}
