using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed partial class ReceiptsWorkspaceTests
{
    [Theory]
    [InlineData(ReceiptWriteOperation.Update, false)] [InlineData(ReceiptWriteOperation.Update, true)]
    [InlineData(ReceiptWriteOperation.Delete, false)] [InlineData(ReceiptWriteOperation.Delete, true)]
    public async Task LargeConflictIsStreamedAndValidatedLikeDetailWithoutChangingInputs(ReceiptWriteOperation operation, bool chunked)
    {
        var latest = JsonSerializer.SerializeToNode(Detail())!.AsObject(); latest["memo"] = new string('あ', 70000);
        var bytes = Encoding.UTF8.GetBytes(LedgerConflictBody("revision_conflict", latest).ToJsonString()); Assert.True(bytes.Length > 65536);
        using var f = new WriteFixture((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict) { Content = chunked ? new StreamContent(new ChunkStream(bytes)) : new ByteArrayContent(bytes) }));
        var input = Snapshot(); var request = WriteRequest(operation, input);
        var error = await Assert.ThrowsAsync<ReceiptRequestException>(() => Write(f, request));
        Assert.Equal("revision_conflict", error.Code); Assert.Equal(Receipt, error.LatestReceipt!.ReceiptId); Assert.Equal(70000, error.LatestReceipt.Memo!.Length);
        Assert.Equal(12.50m, error.LatestReceipt.Groups[0].Details[0].DiscountRate); Assert.Null(error.LatestReceipt.Groups[0].Details[0].TaxRate); Assert.Equal(0m, error.LatestReceipt.Groups[0].Details[1].TaxRate);
        Assert.Null(input.Memo); Assert.Null(input.Groups[0].GroupId); Assert.Single(f.Requests); Assert.NotNull(f.Store.Current);
    }

    [Theory]
    [InlineData("missing-latest")] [InlineData("null-latest")] [InlineData("other-receipt")] [InlineData("missing-null-field")]
    [InlineData("offset-time")] [InlineData("bad-guid")] [InlineData("numeric-decimal")] [InlineData("decimal-precision")]
    [InlineData("duplicate-detail")] [InlineData("null-people")] [InlineData("extra-property")] [InlineData("unknown-code")]
    public async Task MalformedConflictCannotBecomeAdoptableLatest(string invalid)
    {
        var latest = JsonSerializer.SerializeToNode(Detail())!.AsObject(); var payload = LedgerConflictBody("revision_conflict", latest);
        var row = latest["groups"]![0]!["details"]![0]!;
        switch (invalid)
        {
            case "missing-latest": payload.Remove("latest"); break;
            case "null-latest": payload["latest"] = null; break;
            case "other-receipt": latest["receiptId"] = Guid.NewGuid().ToString("D"); break;
            case "missing-null-field": latest.Remove("shopLocation"); break;
            case "offset-time": latest["transactionAt"] = "2026-11-10T23:59:59Z"; break;
            case "bad-guid": latest["revision"] = Guid.NewGuid().ToString("N"); break;
            case "numeric-decimal": row["discountRate"] = 12.5; break;
            case "decimal-precision": row["discountRate"] = "0.123456789012345678901234567891"; break;
            case "duplicate-detail": latest["groups"]![0]!["details"]![1]!["detailId"] = row["detailId"]!.DeepClone(); break;
            case "null-people": row["personIds"] = null; break;
            case "extra-property": latest["totalAmount"] = 100; break;
            case "unknown-code": payload["code"] = "unknown"; break;
        }
        using var f = new WriteFixture((_, _) => Task.FromResult(Json(payload, HttpStatusCode.Conflict)));
        var error = await Assert.ThrowsAsync<ReceiptWriteUncertainException>(() => Write(f, WriteRequest(ReceiptWriteOperation.Update)));
        Assert.Equal(ReceiptWriteFailureKind.InvalidResponse, error.Kind); Assert.Single(f.Requests); Assert.DoesNotContain("latest", error.Message);
    }

    [Theory]
    [InlineData(400, "invalid_input", null)] [InlineData(400, "invalid_input", "master_changed")]
    [InlineData(403, "forbidden", null)] [InlineData(404, "not_found", null)]
    [InlineData(409, "request_conflict", null)] [InlineData(429, "rate_limited", null)] [InlineData(503, "store_unavailable", null)]
    public async Task ConfirmedErrorsKeepFieldReasonAndPermissionsWithoutBusinessRetries(int status, string code, string? reason)
    {
        using var f = new WriteFixture((_, _) => Task.FromResult(Json(status == 409 ? LedgerConflictBody(code, null) : (object)new {
            code, field = "groups[0].details[1].taxRate", reason, message = "非公開SQL", latest = "読んではいけない", permissions = new { currentRole = "Viewer", myPersonId = Owner }
        }, (HttpStatusCode)status)));
        var request = WriteRequest(ReceiptWriteOperation.Update);
        var error = await Assert.ThrowsAsync<ReceiptRequestException>(() => Write(f, request));
        Assert.Equal((HttpStatusCode)status, error.StatusCode); Assert.Equal(code, error.Code); Assert.Equal(reason, error.Reason); Assert.Null(error.LatestReceipt);
        if (status != 409) Assert.Equal("groups[0].details[1].taxRate", error.Field);
        if (status is 403 or 404) { Assert.Equal("Viewer", error.Permissions!.CurrentRole); Assert.Equal(Owner, error.Permissions.MyPersonId); }
        Assert.Single(f.Requests); Assert.NotNull(f.Store.Current); Assert.DoesNotContain("SQL", error.Message);
        var corrected = WriteRequest(ReceiptWriteOperation.Update); Assert.NotEqual(request.RequestId, corrected.RequestId);
    }

    [Theory]
    [InlineData(ReceiptWriteOperation.Create, 200)] [InlineData(ReceiptWriteOperation.Update, 201)] [InlineData(ReceiptWriteOperation.Delete, 200)]
    [InlineData(ReceiptWriteOperation.Create, 302)] [InlineData(ReceiptWriteOperation.Update, 500)]
    public async Task WrongStatusIsNotReportedAsSuccessfulSave(ReceiptWriteOperation operation, int status)
    {
        using var f = new WriteFixture((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        var error = await Assert.ThrowsAsync<ReceiptWriteUncertainException>(() => Write(f, WriteRequest(operation))); Assert.Equal(ReceiptWriteFailureKind.InvalidResponse, error.Kind); Assert.Single(f.Requests);
    }
    [Theory]
    [InlineData(ReceiptWriteOperation.Create)] [InlineData(ReceiptWriteOperation.Update)] [InlineData(ReceiptWriteOperation.Delete)]
    public async Task UnexpectedSuccessBodyDoesNotSupplyInventedIdOrRevision(ReceiptWriteOperation operation)
    {
        using var f = new WriteFixture((_, _) => Task.FromResult(Json(new { receiptId = Receipt, revision = Owner }, operation == ReceiptWriteOperation.Create ? HttpStatusCode.Created : HttpStatusCode.NoContent)));
        var error = await Assert.ThrowsAsync<ReceiptWriteUncertainException>(() => Write(f, WriteRequest(operation))); Assert.Equal(ReceiptWriteFailureKind.InvalidResponse, error.Kind);
    }
    [Theory]
    [InlineData(400)] [InlineData(403)] [InlineData(503)]
    public async Task OtherWriteErrorsKeepTheExistingResponseLimit(int status)
    {
        using var f = new WriteFixture((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StreamContent(new ChunkStream(new byte[65537])) }));
        var error = await Assert.ThrowsAsync<ReceiptWriteUncertainException>(() => Write(f, WriteRequest(ReceiptWriteOperation.Update))); Assert.Equal(ReceiptWriteFailureKind.InvalidResponse, error.Kind); Assert.Single(f.Requests);
    }

    [Theory]
    [InlineData("timeout", ReceiptWriteFailureKind.Timeout)] [InlineData("network", ReceiptWriteFailureKind.Connection)] [InlineData("stream", ReceiptWriteFailureKind.Connection)]
    public async Task UnknownOutcomePreservesRequestAndTokensWithoutRetry(string failure, ReceiptWriteFailureKind expected)
    {
        using var f = new WriteFixture((_, _) => failure switch {
            "timeout" => throw new TaskCanceledException(), "network" => throw new HttpRequestException(),
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StreamContent(new ChunkStream(Encoding.UTF8.GetBytes("{\"code\":\"revision_conflict\",\"latest\":" + new string(' ', 10000)), failAfter: 257)) })
        });
        var request = WriteRequest(ReceiptWriteOperation.Update); var error = await Assert.ThrowsAsync<ReceiptWriteUncertainException>(() => Write(f, request));
        Assert.Equal(expected, error.Kind); Assert.Single(f.Requests); Assert.NotNull(f.Store.Current); Assert.Equal(Owner, request.Revision);
    }
    [Fact]
    public async Task CallerCancellationBeforeAndDuringWriteIsPropagatedAndDoesNotAutoRetry()
    {
        using var source = new CancellationTokenSource(); using var f = new WriteFixture(async (_, ct) => { source.Cancel(); await Task.Delay(Timeout.Infinite, ct); return Written(ReceiptWriteOperation.Delete); });
        var request = WriteRequest(ReceiptWriteOperation.Delete);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Write(f, request, source.Token)); Assert.Single(f.Requests); Assert.NotNull(f.Store.Current);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Write(f, request, source.Token)); Assert.Single(f.Requests);
    }
    [Fact]
    public async Task RejectedRefreshIsStillAuthenticationFailureAndAccountChangeCannotReplayWrite()
    {
        using (var f = new WriteFixture((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/connect/token" ? Json(new { error = "invalid_grant" }, HttpStatusCode.BadRequest) : new(HttpStatusCode.Unauthorized))))
        { await Assert.ThrowsAsync<AuthenticationRejectedException>(() => Write(f, WriteRequest(ReceiptWriteOperation.Delete))); Assert.Null(f.Store.Current); Assert.Equal(2, f.Requests.Count); }
        WriteFixture? fixture = null;
        using var changed = new WriteFixture((request, _) => {
            if (request.RequestUri!.AbsolutePath == "/connect/token") { fixture!.Store.Current = new("other", "other-refresh", DateTimeOffset.UtcNow.AddMinutes(5)); return Task.FromResult(RefreshedTokens()); }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }); fixture = changed;
        await Assert.ThrowsAsync<AuthenticationContextChangedException>(() => Write(changed, WriteRequest(ReceiptWriteOperation.Update)));
        Assert.Equal(2, changed.Requests.Count); Assert.Equal("other", changed.Store.Current!.AccessToken);
    }
}
