using System.Net;
using System.Net.Http.Json;
using Monetail.Windows.Client;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed partial class TransfersWorkspaceTests
{
    private static readonly Guid Ledger = Guid.NewGuid(), Transfer = Guid.NewGuid(), From = Guid.NewGuid(), To = Guid.NewGuid(), Owner = Guid.NewGuid(), Revision = Guid.NewGuid();
    private static readonly DateTime At = new(2026, 11, 10, 23, 59, 59, DateTimeKind.Unspecified);
    private static TransferSummaryInfo Summary(Guid? id = null) => new(id ?? Transfer, At, " 銀行→現金 ", From, To, 123, null);
    private static object Page(TransferSummaryInfo[]? rows = null, string? cursor = null) => new { transfers = rows ?? [Summary()], nextCursor = cursor };
    private static TransferInfo Detail() => new(Transfer, At, " 銀行→現金 ", From, To, int.MaxValue, "", Revision);

    [Fact]
    public async Task TransferListPreservesConditionsOpaqueCursorAndLocalSummaryWithoutMoreRequests()
    {
        var query = new TransferListQuery("all", Owner, From, " ＡＢＣ&+/銀行 ", "opaque/+?&=値");
        using var f = new Fixture(request => {
            Assert.Equal($"/ledgers/{Ledger:D}/transfers", request.RequestUri!.AbsolutePath); var values = Query(request);
            Assert.Equal(5, values.Count); Assert.Equal(query.Month, values["month"]); Assert.Equal(Owner.ToString("D"), values["ownerPersonId"]);
            Assert.Equal(From.ToString("D"), values["paymentMethodId"]); Assert.Equal(query.Q, values["q"]); Assert.Equal(query.Cursor, values["cursor"]);
            return Json(Page(cursor: "next/value"));
        });
        var page = await f.Workspace.ListAsync(query); Assert.Equal(Summary(), Assert.Single(page.Transfers)); Assert.Equal(DateTimeKind.Unspecified, page.Transfers[0].TransactionAt.Kind); Assert.Equal("next/value", page.NextCursor); Assert.Single(f.Requests);
    }
    [Fact]
    public async Task TransferDetailPreservesEightFieldsAndNormalizesOnlyEmptyMemo()
    {
        using var f = new Fixture(request => { Assert.Equal($"/ledgers/{Ledger:D}/transfers/{Transfer:D}", request.RequestUri!.AbsolutePath); return Json(Detail()); });
        var result = await f.Workspace.GetAsync(Transfer); Assert.Equal(Detail() with { Memo = null }, result); Assert.Equal(DateTimeKind.Unspecified, result.TransactionAt.Kind); Assert.Single(f.Requests);
    }

    private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(value) };
    private static Dictionary<string, string> Query(HttpRequestMessage request) => request.RequestUri!.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(field => field.Split('=', 2)).ToDictionary(parts => parts[0], parts => Uri.UnescapeDataString(parts[1]));
    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient http;
        public PeopleWorkspaceTests.Store Store { get; } = new();
        public List<(string Uri, string? Bearer)> Requests { get; } = [];
        public MonetailApiClient Api { get; }
        public ClientSession Session { get; }
        public TransfersWorkspace Workspace { get; }
        public Fixture(Func<HttpRequestMessage, HttpResponseMessage> respond) : this((request, _) => Task.FromResult(respond(request))) { }
        public Fixture(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        {
            var endpoint = ApiEndpoint.Parse("https://synthetic-transfer.example");
            http = new(new Handler((request, token) => { Requests.Add((request.RequestUri!.OriginalString, request.Headers.Authorization?.Parameter)); return respond(request, token); }));
            Api = new(endpoint, http); Session = new(Api, endpoint, Store); Workspace = new(Api, Session, Ledger);
        }
        public void Dispose() => http.Dispose();
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => respond(request, token);
    }
}
