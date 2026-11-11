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
    private static ReceiptSnapshot Snapshot() => new(At, " 店舗 ", null, Payment, 1, "half_up", null, null, null,
        [new(null, Guid.NewGuid(), null, [new(null, 7, "非課税", int.MinValue, 1, -3, 12.50m, null, "Excluded", "Irregular", null, []),
            new(null, 2, "課税0%", int.MaxValue, 1, 0, 0m, 0m, "Variable", "Temporary", "メモ", [Owner])])]);
    private static ReceiptWriteRequest WriteRequest(ReceiptWriteOperation operation, ReceiptSnapshot? snapshot = null) => operation switch {
        ReceiptWriteOperation.Create => ReceiptWriteRequest.Create(Ledger, snapshot ?? Snapshot()),
        ReceiptWriteOperation.Update => ReceiptWriteRequest.Update(Ledger, Receipt, Owner, snapshot ?? Snapshot()),
        _ => ReceiptWriteRequest.Delete(Ledger, Receipt, Owner)
    };
    private static Task Write(WriteFixture fixture, ReceiptWriteRequest request, CancellationToken token = default) => request.Operation switch {
        ReceiptWriteOperation.Create => fixture.Workspace.CreateAsync(request, token), ReceiptWriteOperation.Update => fixture.Workspace.UpdateAsync(request, token), _ => fixture.Workspace.DeleteAsync(request, token)
    };
    private static HttpResponseMessage Written(ReceiptWriteOperation operation) => new(operation == ReceiptWriteOperation.Create ? HttpStatusCode.Created : HttpStatusCode.NoContent);

    [Theory]
    [InlineData(ReceiptWriteOperation.Create)] [InlineData(ReceiptWriteOperation.Update)] [InlineData(ReceiptWriteOperation.Delete)]
    public async Task WriteUsesExactContractAndAcceptsEmptySuccessWithoutInventingIds(ReceiptWriteOperation operation)
    {
        var snapshot = Snapshot(); var groupId = Guid.NewGuid(); var detailId = Guid.NewGuid();
        if (operation == ReceiptWriteOperation.Update) snapshot = snapshot with { Groups = [snapshot.Groups[0] with { GroupId = groupId, Details = [snapshot.Groups[0].Details[0] with { DetailId = detailId }, snapshot.Groups[0].Details[1]] }, snapshot.Groups[0] with { CategoryId = Guid.NewGuid() }] };
        var request = WriteRequest(operation, snapshot); using var f = new WriteFixture((_, _) => Task.FromResult(Written(operation)));
        await Write(f, request); var sent = Assert.Single(f.Requests); using var json = JsonDocument.Parse(sent.Body); var body = json.RootElement;
        Assert.Equal(request.RequestId, body.GetProperty("requestId").GetGuid()); Assert.Equal("old", sent.Bearer);
        Assert.Equal($"/ledgers/{Ledger:D}/receipts" + (operation == ReceiptWriteOperation.Create ? "" : $"/{Receipt:D}"), new Uri(sent.Uri).AbsolutePath);
        Assert.Equal(operation switch { ReceiptWriteOperation.Create => HttpMethod.Post, ReceiptWriteOperation.Update => HttpMethod.Put, _ => HttpMethod.Delete }, sent.Method);
        Assert.Equal(operation == ReceiptWriteOperation.Create ? 11 : operation == ReceiptWriteOperation.Update ? 12 : 2, body.EnumerateObject().Count());
        Assert.Equal(operation != ReceiptWriteOperation.Create, body.TryGetProperty("revision", out var revision)); if (operation != ReceiptWriteOperation.Create) Assert.Equal(Owner, revision.GetGuid());
        Assert.False(body.TryGetProperty("receiptId", out _)); Assert.False(body.TryGetProperty("totalAmount", out _));
        if (operation == ReceiptWriteOperation.Delete) return;
        Assert.Equal("2026-11-10T23:59:59", body.GetProperty("transactionAt").GetString()); Assert.Equal(" 店舗 ", body.GetProperty("shopName").GetString());
        foreach (var field in new[] { "shopLocation", "memo", "recurrenceInterval", "recurrenceUnit" }) Assert.Equal(JsonValueKind.Null, body.GetProperty(field).ValueKind);
        var group = body.GetProperty("groups")[0]; var row = group.GetProperty("details")[0]; var other = group.GetProperty("details")[1];
        Assert.Equal(operation == ReceiptWriteOperation.Update, group.TryGetProperty("groupId", out var gid)); Assert.Equal(operation == ReceiptWriteOperation.Update, row.TryGetProperty("detailId", out var did));
        if (operation == ReceiptWriteOperation.Update)
        {
            Assert.Equal(groupId, gid.GetGuid()); Assert.Equal(detailId, did.GetGuid());
            Assert.False(body.GetProperty("groups")[1].TryGetProperty("groupId", out _));
            Assert.False(body.GetProperty("groups")[1].GetProperty("details")[0].TryGetProperty("detailId", out _));
        }
        Assert.False(other.TryGetProperty("detailId", out _)); Assert.Equal(7, row.GetProperty("displayOrder").GetInt32()); Assert.Equal(int.MinValue, row.GetProperty("unitPrice").GetInt32());
        Assert.Equal("12.50", row.GetProperty("discountRate").GetString()); Assert.Equal(JsonValueKind.Null, row.GetProperty("taxRate").ValueKind);
        Assert.Equal("0", other.GetProperty("taxRate").GetString()); Assert.Equal(Owner.ToString("D"), other.GetProperty("personIds")[0].GetString());
    }

    [Theory]
    [InlineData(ReceiptWriteOperation.Create)] [InlineData(ReceiptWriteOperation.Update)] [InlineData(ReceiptWriteOperation.Delete)]
    public async Task ManualRetryKeepsFrozenBodyAndNewOperationGetsNewRequestId(ReceiptWriteOperation operation)
    {
        var snapshot = Snapshot(); var people = new[] { Owner }; var rows = new[] { snapshot.Groups[0].Details[0] with { PersonIds = people } }; var groups = new[] { snapshot.Groups[0] with { Details = rows } };
        snapshot = snapshot with { Groups = groups }; var request = WriteRequest(operation, snapshot); var calls = 0;
        using var f = new WriteFixture((_, _) => ++calls == 1 ? throw new IOException("synthetic") : Task.FromResult(Written(operation)));
        var error = await Assert.ThrowsAsync<ReceiptWriteUncertainException>(() => Write(f, request)); Assert.Equal(ReceiptWriteFailureKind.Connection, error.Kind); Assert.Single(f.Requests);
        people[0] = Guid.NewGuid(); rows[0] = rows[0] with { Name = "変更後" }; groups[0] = groups[0] with { Memo = "変更後" };
        await Write(f, request); Assert.Equal(f.Requests[0], f.Requests[1]); Assert.NotNull(f.Store.Current);
        var next = WriteRequest(operation, snapshot); Assert.NotEqual(request.RequestId, next.RequestId); await Write(f, next); Assert.NotEqual(f.Requests[1].Body, f.Requests[2].Body);
    }

    [Theory]
    [InlineData(ReceiptWriteOperation.Create)] [InlineData(ReceiptWriteOperation.Update)] [InlineData(ReceiptWriteOperation.Delete)]
    public async Task AuthenticationRefreshOnlyChangesBearerAndRecreatesContent(ReceiptWriteOperation operation)
    {
        using var f = new WriteFixture((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/connect/token" ? RefreshedTokens() : request.Headers.Authorization?.Parameter == "old" ? new(HttpStatusCode.Unauthorized) : Written(operation)));
        var request = WriteRequest(operation); await Write(f, request);
        Assert.Equal(3, f.Requests.Count); Assert.Equal(f.Requests[0].Body, f.Requests[2].Body); Assert.Equal(f.Requests[0].Uri, f.Requests[2].Uri);
        Assert.Equal("updated", f.Requests[2].Bearer); Assert.Equal("rotated", f.Store.Current!.RefreshToken);
    }

    private sealed class WriteFixture : IDisposable
    {
        private readonly HttpClient http;
        public PeopleWorkspaceTests.Store Store { get; } = new();
        public List<(HttpMethod Method, string Uri, string Body, string? Bearer)> Requests { get; } = [];
        public ReceiptsWorkspace Workspace { get; }
        public WriteFixture(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        {
            var endpoint = ApiEndpoint.Parse("https://synthetic-write.example");
            http = new(new Handler(async (request, token) => {
                Requests.Add((request.Method, request.RequestUri!.OriginalString, request.Content is null ? "" : await request.Content.ReadAsStringAsync(token), request.Headers.Authorization?.Parameter));
                return await respond(request, token);
            }));
            var api = new MonetailApiClient(endpoint, http); Workspace = new(api, new(api, endpoint, Store), Ledger);
        }
        public void Dispose() => http.Dispose();
    }
}
