using System.Net;
using System.Text;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed partial class ReceiptsWorkspaceTests
{
    [Theory]
    [InlineData("month")] [InlineData("monthRange")] [InlineData("monthCase")] [InlineData("monthMissing")]
    [InlineData("owner")] [InlineData("payment")] [InlineData("ledger")] [InlineData("receipt")]
    [InlineData("kind")] [InlineData("shopMissing")] [InlineData("categoryMissing")] [InlineData("scope")] [InlineData("extraCategory")]
    [InlineData("qLength")] [InlineData("shopLength")] [InlineData("nullShop")]
    public async Task InvalidReadInputsNeverSendHttp(string kind)
    {
        using var f = new Fixture(_ => throw new InvalidOperationException());
        await Assert.ThrowsAsync<ReceiptInputException>(async () => {
            switch (kind) {
                case "month": await f.Workspace.ListAsync(new("2026-1")); break;
                case "monthRange": await f.Workspace.ListAsync(new("2026-13")); break;
                case "monthCase": await f.Workspace.ListAsync(new("ALL")); break;
                case "monthMissing": await f.Workspace.ListAsync(new(null!)); break;
                case "owner": await f.Workspace.ListAsync(new("all", ownerPersonId: Guid.Empty)); break;
                case "payment": await f.Workspace.ListAsync(new("all", paymentMethodId: Guid.Empty)); break;
                case "ledger": await f.Api.ListReceiptsAsync("old", Guid.Empty, new("all")); break;
                case "receipt": await f.Workspace.GetAsync(Guid.Empty); break;
                case "kind": await f.Workspace.SuggestAsync(new((ReceiptSuggestionKind)99)); break;
                case "shopMissing": await f.Workspace.SuggestAsync(new(ReceiptSuggestionKind.ShopLocation)); break;
                case "categoryMissing": await f.Workspace.SuggestAsync(new(ReceiptSuggestionKind.ItemName, "店舗")); break;
                case "scope": await f.Workspace.SuggestAsync(new(ReceiptSuggestionKind.ShopName, "店舗")); break;
                case "extraCategory": await f.Workspace.SuggestAsync(new(ReceiptSuggestionKind.ShopLocation, "店舗", Guid.NewGuid())); break;
                case "qLength": await f.Workspace.SuggestAsync(new(ReceiptSuggestionKind.ShopName, q: new string('あ', 101))); break;
                case "shopLength": await f.Workspace.AutofillAsync(new(new string('あ', 101), new(2026, 11, 10))); break;
                default: await f.Workspace.AutofillAsync(new(null!, new(2026, 11, 10))); break;
            }
        });
        Assert.Empty(f.Requests); Assert.NotNull(f.Store.Current);
    }

    [Fact]
    public async Task QueriesRetainEmptyValuesUtf16BoundsAndLocalDateWithoutNormalization()
    {
        var text = string.Concat(Enumerable.Repeat("😀", 50));
        using var f = new Fixture(request => {
            var query = Query(request);
            if (request.RequestUri!.AbsolutePath.EndsWith("/receipts", StringComparison.Ordinal)) {
                Assert.Equal("", query["cursor"]); Assert.Equal("", query["q"]); return Json(Page([]));
            }
            if (request.RequestUri.AbsolutePath.EndsWith("receipt-suggestions", StringComparison.Ordinal)) {
                Assert.Equal(text, query["q"]); Assert.Equal(" すーぱー ", query["shopName"]);
                return Json(new { suggestions = Array.Empty<string>(), hasMore = false });
            }
            Assert.Equal(text, query["shopName"]); Assert.Equal("2024-02-29", query["transactionDate"]);
            return Json(new { paymentMethodId = (Guid?)null, taxInclusionMode = (int?)null, taxRoundingMode = (string?)null });
        });
        await f.Workspace.ListAsync(new("all", q: "", cursor: ""));
        await f.Workspace.SuggestAsync(new(ReceiptSuggestionKind.ShopLocation, " すーぱー ", q: text));
        await f.Workspace.AutofillAsync(new(text, new(2024, 2, 29))); Assert.Equal(3, f.Requests.Count);
    }

    [Fact]
    public async Task ListSearchDoesNotInheritSuggestionLengthLimit()
    {
        var text = new string('あ', 1001);
        using var f = new Fixture(request => { Assert.Equal(text, Query(request)["q"]); return Json(Page([])); });
        await f.Workspace.ListAsync(new("0001-01", q: text)); Assert.Single(f.Requests);
    }

    [Fact]
    public async Task HundredLongestUtf16SuggestionsFitExistingBoundAndKeepOrder()
    {
        var values = Enumerable.Range(0, 100).Select(i => i.ToString("000") + new string('店', 97)).Reverse().ToArray();
        using var f = new Fixture(_ => Json(new { suggestions = values, hasMore = true }));
        var result = await f.Workspace.SuggestAsync(new(ReceiptSuggestionKind.ShopName)); Assert.True(result.HasMore); Assert.Equal(values, result.Suggestions);
    }

    [Theory]
    [InlineData("{")] [InlineData("[]")] [InlineData("null")] [InlineData("{\"receipts\":[],\"nextCursor\":null}{}")]
    public async Task MalformedOrNonObjectJsonIsRejectedWithoutRetry(string text)
    {
        using var f = new Fixture(_ => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") });
        await Assert.ThrowsAsync<ApiConnectionException>(() => f.Workspace.ListAsync(new("all"))); Assert.Single(f.Requests); Assert.NotNull(f.Store.Current);
    }

    [Theory]
    [InlineData(false, 0, "floor")] [InlineData(true, 1, "half_up")]
    public async Task AutofillReadsNullableAndPresentPaymentWithoutChoosingAnother(bool payment, int mode, string rounding)
    {
        using var f = new Fixture(_ => Json(new { paymentMethodId = payment ? (Guid?)Payment : null, taxInclusionMode = mode, taxRoundingMode = rounding }));
        var result = await f.Workspace.AutofillAsync(new("店舗", new(2026, 11, 10)));
        Assert.Equal(payment ? (Guid?)Payment : null, result.PaymentMethodId); Assert.Equal(mode, result.TaxInclusionMode); Assert.Equal(rounding, result.TaxRoundingMode);
    }

    [Theory]
    [InlineData(int.MinValue)] [InlineData(int.MaxValue)]
    public async Task SignedJsonIntegersAndExactDecimalExtremesAreNotRecalculated(int total)
    {
        var payload = System.Text.Json.JsonSerializer.SerializeToNode(Detail())!;
        var line = payload["groups"]![0]!["details"]![0]!; line["unitPrice"] = int.MinValue; line["unitDiscountAmount"] = int.MinValue;
        line["quantity"] = int.MaxValue; line["taxRate"] = "79228162514264337593543950335";
        using var f = new Fixture(request => request.RequestUri!.AbsolutePath.EndsWith("/receipts", StringComparison.Ordinal) ? Json(Page([Summary() with { TotalAmount = total }])) : Json(payload));
        Assert.Equal(total, (await f.Workspace.ListAsync(new("all"))).Receipts[0].TotalAmount);
        var result = (await f.Workspace.GetAsync(Receipt)).Groups[0].Details[0]; Assert.Equal(decimal.MaxValue, result.TaxRate);
        Assert.Equal(int.MinValue, result.UnitPrice); Assert.Equal(int.MinValue, result.UnitDiscountAmount); Assert.Equal(int.MaxValue, result.Quantity);
    }

    [Fact]
    public async Task BodyReadFailureMapsToConnectionFailureWithoutRetry()
    {
        using var f = new Fixture(_ => new(HttpStatusCode.OK) { Content = new StreamContent(new ChunkStream(Encoding.UTF8.GetBytes("{\"receipts\":"), failAfter: 1)) });
        await Assert.ThrowsAsync<ApiConnectionException>(() => f.Workspace.ListAsync(new("all"))); Assert.Single(f.Requests); Assert.NotNull(f.Store.Current);
    }

    [Fact]
    public async Task TimeoutIsDistinctFromExplicitCallerCancellation()
    {
        using var f = new Fixture(_ => throw new TaskCanceledException());
        await Assert.ThrowsAsync<ApiConnectionException>(() => f.Workspace.ListAsync(new("all"))); Assert.Single(f.Requests); Assert.NotNull(f.Store.Current);
    }
}
