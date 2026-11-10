using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed partial class ReceiptsWorkspaceTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ValidHundredRowsExceeding64KiBAreReadWithAndWithoutContentLength(bool chunked)
    {
        var rows = Enumerable.Range(0, 100).Select(_ => Summary(Guid.NewGuid()) with { ShopName = new string('店', 100), ShopLocation = new string('地', 100), Memo = new string('記', 1000) }).ToArray();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(Page(rows), new JsonSerializerOptions(JsonSerializerDefaults.Web)); Assert.True(bytes.Length > 65536);
        using var f = new Fixture(_ => new(HttpStatusCode.OK) { Content = chunked ? new StreamContent(new ChunkStream(bytes)) : new ByteArrayContent(bytes) });
        var result = await f.Workspace.ListAsync(new("all")); Assert.Equal(100, result.Receipts.Length);
        Assert.Equal(rows.Select(r => r.ReceiptId), result.Receipts.Select(r => r.ReceiptId)); Assert.Equal(rows[99].Memo, result.Receipts[99].Memo); Assert.Single(f.Requests);
    }

    [Fact]
    public async Task LargeDetailDoesNotIntroduceAMemoOrChildCountLimit()
    {
        var payload = JsonSerializer.SerializeToNode(Detail())!; payload["memo"] = new string('記', 100000);
        var line = payload["groups"]![0]!["details"]![0]!.DeepClone(); var details = new JsonArray();
        for (var i = 0; i < 150; i++) { var copy = line.DeepClone(); copy["detailId"] = Guid.NewGuid(); details.Add(copy); }
        payload["groups"]![0]!["details"] = details;
        using var f = new Fixture(_ => Json(payload)); var result = await f.Workspace.GetAsync(Receipt);
        Assert.Equal(100000, result.Memo!.Length); Assert.Equal(150, result.Groups[0].Details.Length);
    }

    [Theory]
    [InlineData("list")] [InlineData("suggestions")]
    public async Task CountsOverContractLimitAreRejectedRatherThanTruncated(string kind)
    {
        using var f = new Fixture(_ => Json(kind == "list" ? Page(Enumerable.Range(0, 101).Select(_ => Summary(Guid.NewGuid())).ToArray()) :
            (object)new { suggestions = Enumerable.Range(0, 101).Select(i => i.ToString()).ToArray(), hasMore = true }));
        await Assert.ThrowsAsync<ApiConnectionException>(() => Read(f, kind)); Assert.Single(f.Requests);
    }

    [Theory]
    [InlineData("auth")] [InlineData("categories")] [InlineData("receipt-error")]
    public async Task ExistingAndErrorResponsesRetain64KiBBound(string operation)
    {
        using var f = new Fixture(_ => new(operation == "receipt-error" ? HttpStatusCode.BadRequest : HttpStatusCode.OK) { Content = new StreamContent(new ChunkStream(new byte[65537])) });
        await Assert.ThrowsAsync<ApiConnectionException>(() => operation switch {
            "auth" => (Task)f.Api.GetCurrentUserAsync("old"), "categories" => f.Api.ListCategoriesAsync("old", Ledger), _ => f.Workspace.ListAsync(new("all"))
        });
        Assert.Single(f.Requests); Assert.NotNull(f.Store.Current);
    }

    [Theory]
    [InlineData("list")] [InlineData("detail")] [InlineData("suggestions")] [InlineData("autofill")]
    public async Task EmptyResultsAndNullableCompletionRemainTyped(string operation)
    {
        var detail = JsonSerializer.SerializeToNode(Detail())!; detail["groups"] = new JsonArray();
        using var f = new Fixture(_ => Json(operation switch {
            "list" => Page([]), "detail" => detail, "suggestions" => new { suggestions = Array.Empty<string>(), hasMore = false },
            _ => new { paymentMethodId = (Guid?)null, taxInclusionMode = (int?)null, taxRoundingMode = (string?)null }
        }));
        switch (operation) {
            case "list": var page = await f.Workspace.ListAsync(new("all")); Assert.Empty(page.Receipts); Assert.Null(page.NextCursor); break;
            case "detail": Assert.Empty((await f.Workspace.GetAsync(Receipt)).Groups); break;
            case "suggestions": Assert.Empty((await f.Workspace.SuggestAsync(new(ReceiptSuggestionKind.ShopName))).Suggestions); break;
            default: Assert.Equal(new(null, null, null), await f.Workspace.AutofillAsync(new("店舗", new(2026, 11, 10)))); break;
        }
    }

    [Theory]
    [InlineData("list", "missing")] [InlineData("list", "nullRows")] [InlineData("list", "duplicateId")] [InlineData("list", "offset")]
    [InlineData("list", "fractional")] [InlineData("list", "unknown")] [InlineData("list", "uuidN")] [InlineData("list", "duplicateProperty")]
    [InlineData("detail", "missing")] [InlineData("detail", "missingNull")] [InlineData("detail", "nullGroup")] [InlineData("detail", "nullLine")]
    [InlineData("detail", "wrongReceipt")] [InlineData("detail", "duplicatePerson")] [InlineData("detail", "decimalNumber")]
    [InlineData("detail", "decimalPrecision")] [InlineData("detail", "decimalWhitespace")] [InlineData("detail", "fractionalQuantity")]
    [InlineData("detail", "taxMode")] [InlineData("detail", "rounding")] [InlineData("detail", "period")] [InlineData("detail", "occurrence")]
    [InlineData("suggestions", "missing")] [InlineData("suggestions", "nullName")] [InlineData("suggestions", "duplicateName")] [InlineData("suggestions", "hasMoreString")]
    [InlineData("autofill", "missing")] [InlineData("autofill", "emptyId")] [InlineData("autofill", "taxMode")] [InlineData("autofill", "period")]
    public async Task InvalidJsonOrWireTypesDoNotBecomeUsableData(string operation, string error)
    {
        JsonNode payload = JsonSerializer.SerializeToNode(operation switch {
            "list" => Page(), "detail" => Detail(), "suggestions" => new { suggestions = new[] { "候補" }, hasMore = false },
            _ => new { paymentMethodId = (Guid?)Payment, taxInclusionMode = (int?)0, taxRoundingMode = "floor" }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        if (error == "missing") payload.AsObject().Remove(operation switch { "list" => "nextCursor", "detail" => "revision", "suggestions" => "hasMore", _ => "paymentMethodId" });
        else if (operation == "list") switch (error) {
            case "nullRows": payload["receipts"] = null; break;
            case "duplicateId": payload["receipts"]!.AsArray().Add(payload["receipts"]![0]!.DeepClone()); break;
            case "offset": payload["receipts"]![0]!["transactionAt"] = "2026-11-10T23:59:59Z"; break;
            case "fractional": payload["receipts"]![0]!["totalAmount"] = 1.5; break;
            case "unknown": payload["totalCount"] = 1; break;
            case "uuidN": payload["receipts"]![0]!["receiptId"] = Receipt.ToString("N"); break;
        }
        else if (operation == "detail") {
            var line = payload["groups"]![0]!["details"]![0]!;
            switch (error) {
                case "missingNull": payload.AsObject().Remove("shopLocation"); break;
                case "nullGroup": payload["groups"]![0] = null; break;
                case "nullLine": payload["groups"]![0]!["details"]![0] = null; break;
                case "wrongReceipt": payload["receiptId"] = Guid.NewGuid(); break;
                case "duplicatePerson": line["personIds"] = new JsonArray(Owner.ToString(), Owner.ToString()); break;
                case "decimalNumber": line["taxRate"] = 0; break;
                case "decimalPrecision": line["discountRate"] = "0.12345678901234567890123456789"; break;
                case "decimalWhitespace": line["discountRate"] = " 1 "; break;
                case "fractionalQuantity": line["quantity"] = 1.5; break;
                case "taxMode": payload["taxInclusionMode"] = 2; break;
                case "rounding": payload["taxRoundingMode"] = "round"; break;
                case "period": payload["recurrenceInterval"] = 1; break;
                case "occurrence": line["occurrenceType"] = "Unknown"; break;
            }
        }
        else if (operation == "suggestions") switch (error) {
            case "nullName": payload["suggestions"]![0] = null; break;
            case "duplicateName": payload["suggestions"]!.AsArray().Add("候補"); break;
            case "hasMoreString": payload["hasMore"] = "false"; break;
        }
        else switch (error) {
            case "emptyId": payload["paymentMethodId"] = Guid.Empty; break;
            case "taxMode": payload["taxInclusionMode"] = "0"; break;
            case "period": payload["taxRoundingMode"] = null; break;
        }
        var text = payload.ToJsonString(); if (error == "duplicateProperty") text = text.Insert(1, "\"nextCursor\":null,");
        using var f = new Fixture(_ => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") });
        await Assert.ThrowsAsync<ApiConnectionException>(() => Read(f, operation)); Assert.Single(f.Requests); Assert.NotNull(f.Store.Current);
    }

    private static Task Read(Fixture f, string operation, CancellationToken token = default) => operation switch {
        "list" => f.Workspace.ListAsync(new("all"), token), "detail" => f.Workspace.GetAsync(Receipt, token),
        "suggestions" => f.Workspace.SuggestAsync(new(ReceiptSuggestionKind.ShopName), token), _ => f.Workspace.AutofillAsync(new("店舗", new(2026, 11, 10)), token)
    };
    private sealed class ChunkStream(byte[] bytes, int failAfter = int.MaxValue) : Stream
    {
        private readonly MemoryStream inner = new(bytes);
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, Math.Min(count, 257));
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (inner.Position >= failAfter) throw new IOException("synthetic-body-failure");
            return inner.ReadAsync(buffer[..Math.Min(buffer.Length, 257)], token);
        }
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override void Flush() => throw new NotSupportedException(); public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
