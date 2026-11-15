using System.Net;
using System.Text.Json;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed partial class TransfersWorkspaceTests
{
    private static readonly Guid CreatedTransfer = Guid.CreateVersion7();
    private static TransferSnapshot TransferSnapshot() => new(At, " 銀行→現金 ", From, To, int.MaxValue, "");
    private static TransferWriteRequest TransferRequest(TransferWriteOperation operation, TransferSnapshot? snapshot = null) => operation switch
    {
        TransferWriteOperation.Create => TransferWriteRequest.Create(Ledger, snapshot ?? TransferSnapshot()),
        TransferWriteOperation.Update => TransferWriteRequest.Update(Ledger, Transfer, Revision, snapshot ?? TransferSnapshot()),
        _ => TransferWriteRequest.Delete(Ledger, Transfer, Revision)
    };
    private static HttpResponseMessage TransferWritten(TransferWriteOperation operation, string? location = null)
    {
        var response = new HttpResponseMessage(operation == TransferWriteOperation.Create ? HttpStatusCode.Created : HttpStatusCode.NoContent);
        if (operation == TransferWriteOperation.Create) response.Headers.TryAddWithoutValidation("Location", location ?? $"/ledgers/{Ledger:D}/transfers/{CreatedTransfer:D}");
        return response;
    }
    private static Task TransferWrite(TransferWriteFixture f, TransferWriteRequest request, CancellationToken token = default) => request.Operation switch
    {
        TransferWriteOperation.Create => f.Workspace.CreateAsync(request, token),
        TransferWriteOperation.Update => f.Workspace.UpdateAsync(request, token),
        _ => f.Workspace.DeleteAsync(request, token)
    };

    [Fact]
    public async Task TransferCreateUsesSevenFieldsEmptySuccessAndOnlyServerLocationId()
    {
        var snapshot = TransferSnapshot(); var request = TransferWriteRequest.Create(Ledger, snapshot);
        using var f = new TransferWriteFixture((_, _) => Task.FromResult(TransferWritten(request.Operation)));
        var result = await f.Workspace.CreateAsync(request); Assert.Equal(new TransferCreatedInfo(CreatedTransfer), result);
        var sent = Assert.Single(f.Requests); Assert.Equal(HttpMethod.Post, sent.Method); Assert.Equal($"/ledgers/{Ledger:D}/transfers", new Uri(sent.Uri).AbsolutePath);
        using var json = JsonDocument.Parse(sent.Body); var body = json.RootElement;
        Assert.Equal(7, body.EnumerateObject().Count()); Assert.Equal(request.RequestId.ToString("D"), body.GetProperty("requestId").GetString());
        Assert.Equal(4, request.RequestId.Version); Assert.Equal(0x80, request.RequestId.ToByteArray()[8] & 0xc0);
        Assert.Equal("2026-11-10T23:59:59", body.GetProperty("transactionAt").GetString()); Assert.Equal(snapshot.Name, body.GetProperty("name").GetString());
        Assert.Equal(From.ToString("D"), body.GetProperty("fromPaymentMethodId").GetString()); Assert.Equal(To.ToString("D"), body.GetProperty("toPaymentMethodId").GetString());
        Assert.Equal(int.MaxValue, body.GetProperty("amount").GetInt32()); Assert.Equal(JsonValueKind.Null, body.GetProperty("memo").ValueKind);
        Assert.Null(request.TransferId); Assert.Null(request.Revision); Assert.Equal("", snapshot.Memo); Assert.Equal("old", sent.Bearer);
        Assert.Equal("application/json", sent.MediaType);
    }

    [Fact]
    public async Task TransferUpdateAndDeleteUseExactTargetsRevisionAndEmpty204()
    {
        foreach (var operation in new[] { TransferWriteOperation.Update, TransferWriteOperation.Delete })
        {
            var request = TransferRequest(operation); using var f = new TransferWriteFixture((_, _) => Task.FromResult(TransferWritten(operation)));
            await TransferWrite(f, request); var sent = Assert.Single(f.Requests);
            Assert.Equal(operation == TransferWriteOperation.Update ? HttpMethod.Put : HttpMethod.Delete, sent.Method);
            Assert.Equal($"/ledgers/{Ledger:D}/transfers/{Transfer:D}", new Uri(sent.Uri).AbsolutePath);
            using var json = JsonDocument.Parse(sent.Body); var body = json.RootElement;
            Assert.Equal(operation == TransferWriteOperation.Update ? 8 : 2, body.EnumerateObject().Count());
            Assert.Equal(Revision.ToString("D"), body.GetProperty("revision").GetString()); Assert.Equal(request.RequestId, body.GetProperty("requestId").GetGuid());
            Assert.False(body.TryGetProperty("transferId", out _)); Assert.Equal(Transfer, request.TransferId); Assert.Equal(Revision, request.Revision);
        }
    }

    private sealed class TransferWriteFixture : IDisposable
    {
        private readonly HttpClient http;
        public PeopleWorkspaceTests.Store Store { get; } = new();
        public List<(HttpMethod Method, string Uri, string Body, string? Bearer, string? MediaType)> Requests { get; } = [];
        public TransfersWorkspace Workspace { get; }
        public MonetailApiClient Api { get; }
        public ClientSession Session { get; }
        public TransferWriteFixture(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        {
            var endpoint = ApiEndpoint.Parse("https://synthetic-transfer-write.example");
            http = new(new Handler(async (request, token) => {
                Requests.Add((request.Method, request.RequestUri!.OriginalString, request.Content is null ? "" : await request.Content.ReadAsStringAsync(token), request.Headers.Authorization?.Parameter, request.Content?.Headers.ContentType?.MediaType));
                return await respond(request, token);
            }));
            Api = new(endpoint, http); Session = new(Api, endpoint, Store); Workspace = new(Api, Session, Ledger);
        }
        public void Dispose() => http.Dispose();
    }
}
