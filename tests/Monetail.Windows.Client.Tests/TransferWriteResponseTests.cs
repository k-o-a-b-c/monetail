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
    [InlineData(false)] [InlineData(true)]
    public async Task TransferWriteLocationAcceptsCanonicalResourceAndSameOriginAbsoluteUrl(bool absolute)
    {
        var path = $"/ledgers/{Ledger:D}/transfers/{CreatedTransfer:D}".ToUpperInvariant().Replace("/LEDGERS/", "/ledgers/").Replace("/TRANSFERS/", "/transfers/");
        using var f = new TransferWriteFixture((_, _) => Task.FromResult(TransferWritten(TransferWriteOperation.Create, (absolute ? "https://synthetic-transfer-write.example" : "") + path)));
        var result = await f.Workspace.CreateAsync(TransferRequest(TransferWriteOperation.Create)); Assert.Equal(CreatedTransfer, result.TransferId);
        Assert.Single(typeof(TransferCreatedInfo).GetProperties()); Assert.Single(f.Requests);
    }

    [Theory]
    [InlineData("missing")] [InlineData("empty")] [InlineData("duplicate")] [InlineData("foreign-ledger")] [InlineData("empty-id")]
    [InlineData("id-n")] [InlineData("ledger-n")] [InlineData("query")] [InlineData("fragment")] [InlineData("trailing-slash")]
    [InlineData("relative")] [InlineData("foreign-origin")] [InlineData("http")] [InlineData("user-info")] [InlineData("scheme-relative")]
    [InlineData("escaped-id")] [InlineData("backslash")] [InlineData("dot-path")] [InlineData("wrong-resource")] [InlineData("port")]
    public async Task TransferWriteMalformedLocationLeavesResultUncertainWithoutFollowingUrl(string invalid)
    {
        var path = $"/ledgers/{Ledger:D}/transfers/{CreatedTransfer:D}";
        var location = invalid switch {
            "empty" => "", "foreign-ledger" => path.Replace(Ledger.ToString("D"), Guid.NewGuid().ToString("D")),
            "empty-id" => path.Replace(CreatedTransfer.ToString("D"), Guid.Empty.ToString("D")), "id-n" => path.Replace(CreatedTransfer.ToString("D"), CreatedTransfer.ToString("N")),
            "ledger-n" => path.Replace(Ledger.ToString("D"), Ledger.ToString("N")), "query" => path + "?other=1", "fragment" => path + "#section",
            "trailing-slash" => path + "/", "relative" => path[1..], "foreign-origin" => "https://foreign.example" + path,
            "http" => "http://synthetic-transfer-write.example" + path, "user-info" => "https://user@synthetic-transfer-write.example" + path,
            "scheme-relative" => "//synthetic-transfer-write.example" + path, "escaped-id" => path.Replace("/transfers/", "/transfers/%20"),
            "backslash" => path.Replace('/', '\\'), "dot-path" => "https://synthetic-transfer-write.example/x/.." + path,
            "wrong-resource" => path.Replace("/transfers/", "/receipts/"), "port" => "https://synthetic-transfer-write.example:8443" + path, _ => path
        };
        using var f = new TransferWriteFixture((_, _) => {
            var response = new HttpResponseMessage(HttpStatusCode.Created);
            if (invalid != "missing") response.Headers.TryAddWithoutValidation("Location", invalid == "duplicate" ? [path, path] : new[] { location });
            return Task.FromResult(response);
        });
        var request = TransferRequest(TransferWriteOperation.Create); var error = await Assert.ThrowsAsync<TransferWriteUncertainException>(() => f.Workspace.CreateAsync(request));
        Assert.Equal(TransferWriteFailureKind.InvalidResponse, error.Kind); Assert.Null(request.TransferId); Assert.Single(f.Requests); Assert.NotNull(f.Store.Current);
    }

    [Theory]
    [InlineData(TransferWriteOperation.Create, 200)] [InlineData(TransferWriteOperation.Create, 204)] [InlineData(TransferWriteOperation.Update, 201)]
    [InlineData(TransferWriteOperation.Delete, 200)] [InlineData(TransferWriteOperation.Update, 302)] [InlineData(TransferWriteOperation.Delete, 500)]
    public async Task TransferWriteWrongStatusCannotBecomeSuccessfulSave(TransferWriteOperation operation, int status)
    {
        using var f = new TransferWriteFixture((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        var error = await Assert.ThrowsAsync<TransferWriteUncertainException>(() => TransferWrite(f, TransferRequest(operation)));
        Assert.Equal(TransferWriteFailureKind.InvalidResponse, error.Kind); Assert.Single(f.Requests);
    }

    [Theory]
    [InlineData(TransferWriteOperation.Create)] [InlineData(TransferWriteOperation.Update)] [InlineData(TransferWriteOperation.Delete)]
    public async Task TransferWriteNonemptySuccessBodyDoesNotSupplyIdsOrRevision(TransferWriteOperation operation)
    {
        foreach (var text in new[] { " ", "{}", "{\"transferId\":\"" + CreatedTransfer.ToString("D") + "\",\"revision\":\"" + Revision.ToString("D") + "\"}" })
        {
            using var f = new TransferWriteFixture((_, _) => { var response = TransferWritten(operation); response.Content = new StringContent(text); return Task.FromResult(response); });
            var error = await Assert.ThrowsAsync<TransferWriteUncertainException>(() => TransferWrite(f, TransferRequest(operation)));
            Assert.Equal(TransferWriteFailureKind.InvalidResponse, error.Kind); Assert.Single(f.Requests);
        }
    }

    [Theory]
    [InlineData(TransferWriteOperation.Update)] [InlineData(TransferWriteOperation.Delete)]
    public async Task TransferWriteRevisionConflictKeepsStrictDetailWithoutChangingRequest(TransferWriteOperation operation)
    {
        var latest = Detail() with { Revision = Guid.NewGuid(), Memo = "" };
        using var f = new TransferWriteFixture((_, _) => Task.FromResult(Json(TransferConflict("revision_conflict", latest), HttpStatusCode.Conflict)));
        var request = TransferRequest(operation); var error = await Assert.ThrowsAsync<TransferRequestException>(() => TransferWrite(f, request));
        Assert.Equal(HttpStatusCode.Conflict, error.StatusCode); Assert.Equal("revision_conflict", error.Code); Assert.Equal("revision", error.Field);
        Assert.Equal(latest with { Memo = null }, error.LatestTransfer); Assert.Equal(DateTimeKind.Unspecified, error.LatestTransfer!.TransactionAt.Kind);
        Assert.Equal(Revision, request.Revision); Assert.Null(error.Permissions); Assert.DoesNotContain("SQL", error.Message); Assert.Single(f.Requests);
    }

    [Theory]
    [InlineData("missing-latest")] [InlineData("null-latest")] [InlineData("other-id")] [InlineData("missing-memo")] [InlineData("bad-revision")]
    [InlineData("bad-time")] [InlineData("bad-amount")] [InlineData("extra-field")] [InlineData("unknown-code")] [InlineData("wrong-field-type")]
    [InlineData("empty-guid")] [InlineData("same-payments")] [InlineData("memo-newline")] [InlineData("name-blank")] [InlineData("request-latest")]
    public async Task TransferWriteRejectsMalformedConflictInsteadOfAdoptingLatest(string invalid)
    {
        var payload = TransferConflict("revision_conflict", Detail()); var latest = payload["latest"]!;
        switch (invalid) {
            case "missing-latest": payload.Remove("latest"); break; case "null-latest": payload["latest"] = null; break;
            case "other-id": latest["transferId"] = Guid.NewGuid().ToString("D"); break; case "missing-memo": latest.AsObject().Remove("memo"); break;
            case "bad-revision": latest["revision"] = Revision.ToString("N"); break; case "bad-time": latest["transactionAt"] = "2026-11-10T23:59:59Z"; break;
            case "bad-amount": latest["amount"] = "123"; break; case "extra-field": latest["createdAtUtc"] = "private"; break;
            case "unknown-code": payload["code"] = "other"; break; case "wrong-field-type": payload["field"] = 1; break;
            case "empty-guid": latest["fromPaymentMethodId"] = Guid.Empty.ToString("D"); break; case "same-payments": latest["toPaymentMethodId"] = From.ToString("D"); break;
            case "memo-newline": latest["memo"] = "a\nb"; break; case "name-blank": latest["name"] = "\u3000"; break;
            default: payload["code"] = "request_conflict"; break;
        }
        using var f = new TransferWriteFixture((_, _) => Task.FromResult(Json(payload, HttpStatusCode.Conflict)));
        var error = await Assert.ThrowsAsync<TransferWriteUncertainException>(() => f.Workspace.UpdateAsync(TransferRequest(TransferWriteOperation.Update)));
        Assert.Equal(TransferWriteFailureKind.InvalidResponse, error.Kind); Assert.Single(f.Requests);
    }

    [Theory]
    [InlineData(TransferWriteOperation.Create)] [InlineData(TransferWriteOperation.Update)] [InlineData(TransferWriteOperation.Delete)]
    public async Task TransferWriteRequestConflictIsConfirmedWithExplicitNullLatest(TransferWriteOperation operation)
    {
        using var f = new TransferWriteFixture((_, _) => Task.FromResult(Json(TransferConflict("request_conflict", null), HttpStatusCode.Conflict)));
        var request = TransferRequest(operation); var error = await Assert.ThrowsAsync<TransferRequestException>(() => TransferWrite(f, request));
        Assert.Equal("request_conflict", error.Code); Assert.Null(error.LatestTransfer); Assert.Null(error.Permissions); Assert.DoesNotContain("SQL", error.Message);
        Assert.Equal(Revision, operation == TransferWriteOperation.Create ? Revision : request.Revision); Assert.Single(f.Requests);
    }

    [Theory]
    [InlineData(400, "invalid_input")] [InlineData(403, "forbidden")] [InlineData(404, "not_found")] [InlineData(429, "rate_limited")] [InlineData(503, "store_unavailable")]
    public async Task TransferWriteConfirmedErrorsKeepFieldAndPermissionsButNoPrivateLatest(int status, string code)
    {
        using var f = new TransferWriteFixture((_, _) => Task.FromResult(Json(new { code, message = "非公開SQL", field = "amount", latest = "非公開", permissions = new { currentRole = "Viewer", myPersonId = Owner } }, (HttpStatusCode)status)));
        var error = await Assert.ThrowsAsync<TransferRequestException>(() => f.Workspace.UpdateAsync(TransferRequest(TransferWriteOperation.Update)));
        Assert.Equal((HttpStatusCode)status, error.StatusCode); Assert.Equal(code, error.Code); Assert.Equal("amount", error.Field); Assert.Null(error.LatestTransfer); Assert.DoesNotContain("SQL", error.Message);
        if (status is 403 or 404) Assert.Equal(new("Viewer", Owner), error.Permissions); else Assert.Null(error.Permissions);
        Assert.Single(f.Requests); Assert.NotNull(f.Store.Current);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task TransferWriteErrorsIncludingConflictKeep64KiBBoundary(bool chunked)
    {
        foreach (var status in new[] { HttpStatusCode.Conflict, HttpStatusCode.Forbidden })
        foreach (var length in new[] { 65536, 65537 })
        {
            var payload = JsonSerializer.Serialize(status == HttpStatusCode.Conflict ? TransferConflict("request_conflict", null) : (object)new { code = "forbidden" }, Web);
            var bytes = Encoding.UTF8.GetBytes(new string(' ', length - Encoding.UTF8.GetByteCount(payload)) + payload);
            using var f = new TransferWriteFixture((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = chunked ? new StreamContent(new ChunkStream(bytes)) : new ByteArrayContent(bytes) }));
            if (length == 65536) Assert.Equal(status, (await Assert.ThrowsAsync<TransferRequestException>(() => f.Workspace.DeleteAsync(TransferRequest(TransferWriteOperation.Delete)))).StatusCode);
            else Assert.Equal(TransferWriteFailureKind.InvalidResponse, (await Assert.ThrowsAsync<TransferWriteUncertainException>(() => f.Workspace.DeleteAsync(TransferRequest(TransferWriteOperation.Delete)))).Kind);
            Assert.Single(f.Requests);
        }
    }

    [Fact]
    public async Task TransferWritePostCannotExposeRevisionConflictAndMalformedJsonIsUncertain()
    {
        foreach (var text in new[] { TransferConflict("revision_conflict", Detail()).ToJsonString(), "{", "[]", "null", "{\"code\":\"request_conflict\",\"code\":\"request_conflict\",\"latest\":null}" })
        {
            using var f = new TransferWriteFixture((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent(text) }));
            Assert.Equal(TransferWriteFailureKind.InvalidResponse, (await Assert.ThrowsAsync<TransferWriteUncertainException>(() => f.Workspace.CreateAsync(TransferRequest(TransferWriteOperation.Create)))).Kind);
        }
    }

    private static JsonObject TransferConflict(string code, TransferInfo? latest) => new()
    {
        ["code"] = code, ["message"] = "非公開SQL", ["field"] = "revision", ["latest"] = JsonSerializer.SerializeToNode(latest, Web), ["permissions"] = null
    };
}
