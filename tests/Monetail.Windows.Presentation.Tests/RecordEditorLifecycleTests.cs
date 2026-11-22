using System.Net;
using System.Net.Http.Json;
using Monetail.Windows.Client;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Client.Transfers;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Presentation.Receipts;
using Monetail.Windows.Presentation.Records;
using Monetail.Windows.Presentation.Transfers;
using Xunit;

namespace Monetail.Windows.Presentation.Tests;

public sealed class RecordEditorLifecycleTests
{
    [Theory]
    [InlineData(TransferWriteOperation.Create)]
    [InlineData(TransferWriteOperation.Update)]
    [InlineData(TransferWriteOperation.Delete)]
    public async Task WriteCompletionUsesTheOriginalRequestAndDoesNotFetchDetails(TransferWriteOperation operation)
    {
        using var f = new Fixture();
        var model = f.Transfer();
        if (operation == TransferWriteOperation.Delete)
            model.PrepareDeleteWrite(f.Context.LedgerId);
        else
            model.PrepareWrite(f.Context.LedgerId, model.PrepareSave(operation == TransferWriteOperation.Create));
        var request = model.PendingRequest;
        var snapshot = model.PendingSnapshot;
        var result = await model.SendPendingAsync();
        Assert.NotNull(result.Completion);
        Assert.Same(request, result.Completion.Request);
        Assert.Same(snapshot, result.Completion.Snapshot);
        Assert.Equal(operation, result.Completion.Request.Operation);
        Assert.Null(model.PendingRequest);
        Assert.False(model.IsSending);
        Assert.Single(f.Writes);
        Assert.Empty(f.Reads);
    }

    [Fact]
    public async Task UnknownResultKeepsTheSameRequestUntilExplicitRetry()
    {
        using var f = new Fixture();
        var count = 0;
        f.Write = _ => ++count == 1 ? throw new HttpRequestException("cut") : new(HttpStatusCode.NoContent);
        var model = f.Transfer();
        model.PrepareWrite(f.Context.LedgerId, model.PrepareSave(false));
        var request = model.PendingRequest;
        var snapshot = model.PendingSnapshot;
        Assert.Null((await model.SendPendingAsync()).Completion);
        Assert.Same(request, model.PendingRequest);
        Assert.Same(snapshot, model.PendingSnapshot);
        Assert.True(model.HasUnknownWrite);
        Assert.True(model.CanRetryPendingWrite);
        Assert.False(model.SaveCommand.CanExecute(null));
        Assert.Single(f.Writes);
        var result = await model.SendPendingAsync();
        Assert.Same(request, result.Completion!.Request);
        Assert.Equal(2, f.Writes.Count);
        Assert.Equal(f.Writes[0], f.Writes[1]);
        Assert.Empty(f.Reads);
    }

    [Theory]
    [InlineData("revision_conflict")]
    [InlineData("request_conflict")]
    public async Task ConflictDoesNotReplaceInputOrRevision(string code)
    {
        using var f = new Fixture();
        var model = f.Transfer();
        var revision = model.Editor.Revision;
        model.Editor.Draft.Name = "未保存";
        var latest = f.Saved with
        {
            Name = "最新",
            Revision = Guid.NewGuid()
        };
        f.Write = _ => new(HttpStatusCode.Conflict)
        {
            Content = JsonContent.Create(new
            {
                code,
                message = "internal",
                field = (string?)null,
                latest = code == "revision_conflict" ? latest : null,
                permissions = (object?)null
            })
        };
        model.PrepareWrite(f.Context.LedgerId, model.PrepareSave(false));
        var result = await model.SendPendingAsync();
        Assert.Null(result.Completion);
        Assert.Equal(code == "revision_conflict", result.OfferLatest);
        Assert.Equal("未保存", model.Editor.Draft.Name);
        Assert.Equal(revision, model.Editor.Revision);
        Assert.Equal(code == "revision_conflict", model.CanAdoptLatest);
        Assert.Equal(code == "request_conflict", model.IsStopped);
        Assert.Empty(f.Reads);
    }

    [Fact]
    public async Task AuthenticationRecoveryRetainsTheSameDraftAndRequestWithoutResending()
    {
        using var f = new Fixture();
        var model = f.Transfer();
        model.PrepareWrite(f.Context.LedgerId, model.PrepareSave(false));
        var request = model.PendingRequest;
        var draft = model.Editor.Draft;
        var revision = model.Editor.Revision;
        model.SuspendForAuthentication();
        Assert.True(model.AwaitingLogin);
        await model.LoadRecoveryMastersAsync(default);
        Assert.False(model.RecoveryBlocked);
        Assert.Same(request, model.PendingRequest);
        Assert.Same(draft, model.Editor.Draft);
        Assert.Equal(revision, model.Editor.Revision);
        Assert.True(model.CanRetryPendingWrite);
        Assert.Empty(f.Writes);
        Assert.DoesNotContain(f.Reads, p => p.Contains("/transfers/"));
        model.Release();
        model.Release();
        Assert.Null(model.PendingRequest);
        Assert.Null(model.Context);
    }

    [Fact]
    public async Task ReceiptRecoveryFailureAndRetryKeepTheSameUnappliedInputAndOldRevision()
    {
        using var f = new Fixture();
        var model = await ReceiptEditorViewModel.OpenAsync(f.Context, null, default);
        model.Editor.AddCategory(f.Category);
        model.LoadDetailInput();
        var input = model.DetailInput;
        var draft = model.Editor.Draft;
        model.UpdateHeader("2026/", "9:", "店", "", "保持", f.From, 0, "floor", "", "Month");
        Assert.Null(model.ConfirmedDate);
        var reads = f.Reads.Count;
        model.SuspendForAuthentication();
        f.FailMasters = true;
        await model.LoadRecoveryMastersAsync(default);
        Assert.True(model.IsReadOnly);
        Assert.True(model.ShowRetryMasters);
        Assert.Equal("マスタ情報を取得できませんでした", model.RecoveryMessage);
        Assert.Same(input, model.DetailInput);
        f.FailMasters = false;
        await model.LoadRecoveryMastersAsync(default);
        Assert.False(model.IsReadOnly);
        Assert.Same(draft, model.Editor.Draft);
        Assert.Same(input, model.DetailInput);
        Assert.Equal("保持", model.Editor.Header.Memo);
        Assert.Null(model.ConfirmedDate);
        Assert.DoesNotContain(f.Reads.Skip(reads), p => p.Contains("/receipts/"));
        Assert.Empty(f.Writes);
        model.Discard();
        model.Discard();
        Assert.Null(model.Context);
        Assert.Null(model.DetailInput);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient http;
        internal RecordEditorContext Context
        {
            get;
        }
        internal Guid Person = Guid.NewGuid(), From = Guid.NewGuid(), To = Guid.NewGuid(), Category = Guid.NewGuid();
        internal TransferInfo Saved
        {
            get;
        }
        internal List<string> Reads { get; } = [];
        internal List<string> Writes { get; } = [];
        internal Func<HttpRequestMessage, HttpResponseMessage>? Write;
        internal bool FailMasters;
        internal Fixture()
        {
            var endpoint = ApiEndpoint.Parse("https://synthetic.example");
            var ledger = Guid.NewGuid();
            Saved = new(Guid.CreateVersion7(), new(2026, 11, 22, 12, 0, 0), "振替", From, To, 100, null, Guid.NewGuid());
            http = new(new Handler(request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (request.Method != HttpMethod.Get && !path.EndsWith("/open"))
                {
                    Writes.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                    if (Write is not null)
                        return Write(request);
                    var response = new HttpResponseMessage(request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.NoContent);
                    if (request.Method == HttpMethod.Post)
                        response.Headers.Location = new($"/ledgers/{ledger:D}/transfers/{Saved.TransferId:D}", UriKind.Relative);
                    return response;
                }
                Reads.Add(path);
                if (path.EndsWith("/open"))
                    return Json(new LedgerInfo(ledger, "家計簿", "Admin", Guid.NewGuid().ToString("D")));
                if (FailMasters)
                    return new(HttpStatusCode.ServiceUnavailable);
                if (path.EndsWith("/people"))
                    return Json(People());
                if (path.EndsWith("/payment-methods"))
                    return Json(Payments());
                if (path.EndsWith("/categories"))
                    return Json(new CategoriesInfo([new(Category, "科目", "Expense", "Exempt", null, null, 0, Guid.NewGuid())], Guid.NewGuid(), "Admin"));
                if (path.EndsWith("/tax-rates"))
                    return Json(new
                    {
                        rates = Array.Empty<object>()
                    });
                throw new InvalidOperationException(path);
            }));
            var api = new MonetailApiClient(endpoint, http);
            Context = new(api, new(api, endpoint, new Tokens()), new(), ledger, Guid.NewGuid(), endpoint.Value, default);
        }
        private PeopleInfo People() => new([new(Person, "本人", null, null, 0, Guid.NewGuid())], Person, "Admin", Guid.NewGuid());
        private PaymentMethodsInfo Payments() => new([new(From, Person, "現金", 0, null, null, Guid.NewGuid()), new(To, Person, "銀行", 1, null, null, Guid.NewGuid())], [new(Person, Guid.NewGuid())], Person, "Admin");
        internal TransferEditorViewModel Transfer()
        {
            var model = new TransferEditorViewModel(TransferEditorState.Edit(Saved), static () => new(false, false, false, false, true),
                _ => "支払方法", (_, _) => throw new InvalidOperationException(), (_, _) => throw new InvalidOperationException())
            {
                Context = Context
            };
            model.UpdateMasters(People(), Payments());
            return model;
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
        public void Dispose() => http.Dispose();
        private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(send(request));
        }
        private sealed class Tokens : ITokenStore
        {
            private TokenSet? current = new("access", "refresh", DateTimeOffset.UtcNow.AddMinutes(10));
            public TokenSet? Load(ApiEndpoint endpoint) => current;
            public void Save(ApiEndpoint endpoint, TokenSet value) => current = value;
            public void Clear() => current = null;
        }
    }
}
