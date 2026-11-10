using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Monetail.Windows.Client;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed partial class ReceiptsWorkspaceTests
{
    private static readonly Guid Ledger = Guid.NewGuid(), Receipt = Guid.NewGuid(), Payment = Guid.NewGuid(), Owner = Guid.NewGuid();
    private static readonly DateTime At = new(2026, 11, 10, 23, 59, 59, DateTimeKind.Unspecified);
    private static ReceiptSummaryInfo Summary(Guid? id = null) => new(id ?? Receipt, At, "店舗", null, Payment, -123, null);
    private static object Page(ReceiptSummaryInfo[]? rows = null, string? cursor = null) => new { receipts = rows ?? [Summary()], nextCursor = cursor };
    private static object Detail() => new
    {
        receiptId = Receipt, transactionAt = "2026-11-10T23:59:59", shopName = "店舗", shopLocation = (string?)null, paymentMethodId = Payment,
        taxInclusionMode = 1, taxRoundingMode = "half_up", recurrenceInterval = (int?)null, recurrenceUnit = (string?)null, memo = (string?)null, revision = Guid.NewGuid(),
        groups = new[] { new { groupId = Guid.NewGuid(), categoryId = Guid.NewGuid(), memo = (string?)null, details = new[] {
            new { detailId = Guid.NewGuid(), displayOrder = 7, name = "非課税", unitPrice = -100, quantity = 2, unitDiscountAmount = -3, discountRate = "12.50", taxRate = (string?)null,
                expenseType = "Excluded", occurrenceType = "Irregular", memo = (string?)null, personIds = Array.Empty<Guid>() },
            new { detailId = Guid.NewGuid(), displayOrder = 2, name = "課税0%", unitPrice = 10, quantity = 1, unitDiscountAmount = 0, discountRate = "0", taxRate = (string?)"0",
                expenseType = "Variable", occurrenceType = "Temporary", memo = (string?)"メモ", personIds = new[] { Owner } }
        } } }
    };

    [Fact]
    public async Task ListKeepsAllFiveConditionsAndOpaqueCursorWithoutFurtherRequests()
    {
        var query = new ReceiptListQuery("all", Owner, Payment, " ＡＢＣ&+/店舗 ", "opaque/+?&=値");
        using var f = new Fixture(request => {
            Assert.Equal($"/ledgers/{Ledger:D}/receipts", request.RequestUri!.AbsolutePath); var values = Query(request);
            Assert.Equal(5, values.Count); Assert.Equal(query.Month, values["month"]); Assert.Equal(Owner.ToString("D"), values["ownerPersonId"]);
            Assert.Equal(Payment.ToString("D"), values["paymentMethodId"]); Assert.Equal(query.Q, values["q"]); Assert.Equal(query.Cursor, values["cursor"]);
            return Json(Page(cursor: "next/value"));
        });
        var page = await f.Workspace.ListAsync(query); var row = Assert.Single(page.Receipts);
        Assert.Equal(Summary(), row); Assert.Equal(DateTimeKind.Unspecified, row.TransactionAt.Kind); Assert.Equal("next/value", page.NextCursor);
        Assert.Null(row.ShopLocation); Assert.Null(row.Memo); Assert.Single(f.Requests);
    }

    [Fact]
    public async Task DetailKeepsEveryHierarchyAndDoesNotConvertLocalTimeOrNullTax()
    {
        using var f = new Fixture(_ => Json(Detail())); var receipt = await f.Workspace.GetAsync(Receipt);
        Assert.Equal(At, receipt.TransactionAt); Assert.Equal(DateTimeKind.Unspecified, receipt.TransactionAt.Kind);
        Assert.Null(receipt.ShopLocation); Assert.Null(receipt.Memo); Assert.Null(receipt.RecurrenceInterval); Assert.Null(receipt.RecurrenceUnit);
        var group = Assert.Single(receipt.Groups); Assert.Null(group.Memo); Assert.Equal(new[] { 7, 2 }, group.Details.Select(d => d.DisplayOrder));
        Assert.Equal(12.50m, group.Details[0].DiscountRate); Assert.Null(group.Details[0].TaxRate); Assert.Empty(group.Details[0].PersonIds);
        Assert.Equal(0m, group.Details[1].TaxRate); Assert.Equal(new[] { Owner }, group.Details[1].PersonIds); Assert.Single(f.Requests);
    }

    [Fact]
    public async Task SuggestionsAndAutofillPreserveRawStringsAndAllNullableFields()
    {
        using var f = new Fixture(request => request.RequestUri!.AbsolutePath.EndsWith("receipt-autofill", StringComparison.Ordinal)
            ? Json(new { paymentMethodId = (Guid?)null, taxInclusionMode = (int?)null, taxRoundingMode = (string?)null })
            : Json(new { suggestions = new[] { "", " ", "スーパー", "ｽｰﾊﾟｰ" }, hasMore = false }));
        foreach (var kind in Enum.GetValues<ReceiptSuggestionKind>())
        {
            var result = await f.Workspace.SuggestAsync(new(kind, kind == ReceiptSuggestionKind.ShopName ? null : "店舗", kind == ReceiptSuggestionKind.ItemName ? Guid.NewGuid() : null, "す"));
            Assert.Equal(new[] { "", " ", "スーパー", "ｽｰﾊﾟｰ" }, result.Suggestions); Assert.False(result.HasMore);
        }
        Assert.Equal(new(null, null, null), await f.Workspace.AutofillAsync(new("店舗", new(2026, 11, 10)))); Assert.Equal(4, f.Requests.Count);
    }

    private static HttpResponseMessage Json(object payload, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(payload) };
    private static Dictionary<string, string> Query(HttpRequestMessage request) => request.RequestUri!.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(field => field.Split('=', 2)).ToDictionary(parts => parts[0], parts => Uri.UnescapeDataString(parts[1]));
    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient http;
        public PeopleWorkspaceTests.Store Store { get; } = new();
        public List<(string Uri, string? Bearer)> Requests { get; } = [];
        public MonetailApiClient Api { get; }
        public ClientSession Session { get; }
        public ReceiptsWorkspace Workspace { get; }
        public Fixture(Func<HttpRequestMessage, HttpResponseMessage> respond) : this((request, _) => Task.FromResult(respond(request))) { }
        public Fixture(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        {
            var endpoint = ApiEndpoint.Parse("https://synthetic-receipt.example");
            http = new(new Handler((request, token) => {
                Requests.Add((request.RequestUri!.OriginalString, request.Headers.Authorization?.Parameter)); return respond(request, token);
            }));
            Api = new(endpoint, http); Session = new(Api, endpoint, Store); Workspace = new(Api, Session, Ledger);
        }
        public void Dispose() => http.Dispose();
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => respond(request, token);
    }
}
