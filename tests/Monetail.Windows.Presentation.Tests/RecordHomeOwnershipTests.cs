using System.Net;
using System.Net.Http.Json;
using Monetail.Windows.Client;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Presentation.Lists;
using Monetail.Windows.Presentation.Workspace;
using Xunit;

namespace Monetail.Windows.Presentation.Tests;

public sealed class RecordHomeOwnershipTests
{
    [Fact]
    public async Task ListsShareMastersAndCancellingOneWaitDoesNotCancelTheOther()
    {
        using var fixture = new Fixture();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken shared = default;
        fixture.BeforePeople = async token =>
        {
            shared = token;
            await gate.Task.WaitAsync(token);
        };
        using var home = fixture.Create();
        var read = home.StartAsync();
        home.Receipts.Suspend();
        Assert.False(shared.IsCancellationRequested);
        gate.SetResult();
        await read;
        Assert.False(home.Receipts.HasLoaded);
        Assert.Single(home.Transfers.Rows);
        await home.ResumeAsync();
        Assert.Single(home.Receipts.Rows);
        Assert.Equal(1, fixture.Count("people"));
        Assert.Equal(1, fixture.Count("payment-methods"));
        Assert.Equal(1, fixture.Count("receipts"));
        Assert.Equal(1, fixture.Count("transfers"));
    }

    [Fact]
    public async Task HomeSuspensionKeepsSharedReadAndResumeDoesNotDuplicateIt()
    {
        using var fixture = new Fixture();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken shared = default;
        fixture.BeforePeople = async token =>
        {
            shared = token;
            await gate.Task.WaitAsync(token);
        };
        using var home = fixture.Create();
        var read = home.StartAsync();
        home.Suspend();
        await read;
        Assert.False(shared.IsCancellationRequested);
        var resumed = home.ResumeAsync();
        Assert.Equal(1, fixture.Count("people"));
        gate.SetResult();
        await resumed;
        Assert.Single(home.Receipts.Rows);
        Assert.Single(home.Transfers.Rows);
        Assert.Equal(1, fixture.Count("payment-methods"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposalOrWindowCancellationStopsSharedReadWithoutClearingOtherScreensCache(bool windowClosed)
    {
        using var fixture = new Fixture();
        using var window = new CancellationTokenSource();
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.BeforePeople = async token =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            finally { cancelled.TrySetResult(); }
        };
        var categories = new CategoriesInfo([], Guid.NewGuid(), "Admin");
        fixture.Cache.UpdateCategories(fixture.Ledger, categories);
        using var home = fixture.Create(window.Token);
        var read = home.StartAsync(window.Token);
        if (windowClosed)
            window.Cancel();
        else
            home.Dispose();
        await cancelled.Task;
        await read;
        home.Dispose();
        home.Dispose();
        await home.ResumeAsync();
        Assert.Empty(home.Receipts.Rows);
        Assert.Empty(home.Transfers.Rows);
        Assert.Equal(1, fixture.Count("people"));
        Assert.Equal(0, fixture.Count("payment-methods"));
        Assert.Same(categories, fixture.Cache.Categories(fixture.Ledger));
    }

    [Fact]
    public async Task LateMasterResponseCannotUpdateDisposedHomeOrRecreatedContext()
    {
        using var fixture = new Fixture();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.BeforePeople = _ => gate.Task;
        var old = fixture.Create();
        var read = old.StartAsync();
        old.Dispose();
        fixture.Cache.Clear();
        fixture.BeforePeople = null;
        using var current = fixture.Create();
        var reload = current.StartAsync();
        gate.SetResult();
        await Task.WhenAll(read, reload);
        Assert.Empty(old.Receipts.Rows);
        Assert.Empty(old.Transfers.Rows);
        Assert.NotNull(fixture.Cache.People(fixture.Ledger));
        Assert.Equal(2, fixture.Count("people"));
        Assert.Single(current.Receipts.Rows);
        Assert.Single(current.Transfers.Rows);
    }

    [Fact]
    public async Task MasterNotificationRefreshesBothListsAndFiltersWithoutChangingSearchSelectionOrPosition()
    {
        using var fixture = new Fixture();
        using var home = fixture.Create();
        fixture.Cache.Changed += home.MastersChanged;
        try
        {
            await home.StartAsync();
            Assert.Null(home.Receipts.Failure);
            Assert.Null(home.Transfers.Failure);
            home.Keyword = "未確定キーワード";
            home.MonthText = "2027-";
            home.Search.Receipts.OwnerPersonId = fixture.Person;
            home.Search.Receipts.PaymentMethodId = home.Payments[1].Id;
            home.Receipts.SelectedItem = home.Receipts.Rows[0];
            home.Transfers.SelectedItem = home.Transfers.Rows[0];
            home.Receipts.ViewPosition = new(12, 34);
            home.Transfers.ViewPosition = new(56, 78);
            var receipt = home.Receipts.SelectedItem;
            var transfer = home.Transfers.SelectedItem;
            var changes = 0;
            home.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(home.Owners)) changes++; };
            fixture.Cache.UpdatePeople(fixture.Ledger, fixture.People with
            {
                People = [fixture.People.People[0] with { Name = "変更後" }]
            });
            Assert.Equal(1, changes);
            Assert.Equal("（変更後）現金", receipt.PaymentName);
            Assert.Equal("（変更後）現金", transfer.FromName);
            Assert.Equal("変更後", home.Owners[1].Name);
            Assert.Equal("（変更後）現金", home.Payments[1].Name);
            fixture.Cache.UpdateIdentity(fixture.Ledger, fixture.Person);
            Assert.Equal("現金", receipt.PaymentName);
            Assert.Equal("現金", transfer.FromName);
            Assert.Equal("現金", home.Payments[1].Name);
            fixture.Cache.UpdatePeople(Guid.NewGuid(), fixture.People);
            Assert.Equal(2, changes);
            Assert.Same(receipt, home.Receipts.SelectedItem);
            Assert.Same(transfer, home.Transfers.SelectedItem);
            Assert.Equal(new ListViewportPosition(12, 34), home.Receipts.ViewPosition);
            Assert.Equal(new ListViewportPosition(56, 78), home.Transfers.ViewPosition);
            Assert.Equal("未確定キーワード", home.Keyword);
            Assert.Equal("2027-", home.MonthText);
            Assert.Equal(fixture.Person, home.Owner?.Id);
            Assert.Equal(home.Receipts.Rows[0].Receipt.PaymentMethodId, home.Payment?.Id);
            Assert.Equal(4, fixture.Paths.Count);
            home.Dispose();
            fixture.Cache.UpdateIdentity(fixture.Ledger, null);
            Assert.Equal(2, changes);
        }
        finally { fixture.Cache.Changed -= home.MastersChanged; }
    }

    [Fact]
    public async Task RebindUsesNewApiAndSessionButKeepsSearchRowsSelectionAndViewport()
    {
        using var old = new Fixture("old-access");
        using var current = new Fixture("new-access");
        using var home = old.Create();
        await home.StartAsync();
        Assert.Null(home.Receipts.Failure);
        Assert.Null(home.Transfers.Failure);
        var receipt = home.Receipts.Rows[0];
        home.Receipts.SelectedItem = receipt;
        home.Receipts.ViewPosition = new(11, 22);
        home.MonthText = "未確定";
        var search = home.Search;
        // 接続先再読込を伴う同一本人の401復帰。Shellの照合後に通信だけを差し替える。
        old.Cache.Clear();
        home.Suspend();
        home.Rebind(current.Api, current.Session, CancellationToken.None);
        Assert.Same(search, home.Search);
        Assert.Same(receipt, home.Receipts.SelectedItem);
        Assert.Equal(new ListViewportPosition(11, 22), home.Receipts.ViewPosition);
        Assert.Equal("未確定", home.MonthText);
        Assert.Empty(current.Paths);
        await home.ReloadAsync();
        Assert.Equal(4, old.Paths.Count);
        Assert.Equal(4, current.Paths.Count);
        Assert.All(current.AccessTokens, access => Assert.Equal("new-access", access));
        Assert.NotNull(old.Cache.People(old.Ledger));
        Assert.Null(home.Receipts.Failure);
        Assert.Null(home.Transfers.Failure);
    }

    [Fact]
    public async Task RebindCancelsOldSharedReadAndRejectsItsLateResult()
    {
        using var old = new Fixture();
        using var current = new Fixture("new-access");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken shared = default;
        old.BeforePeople = token => { shared = token; return gate.Task; };
        using var home = old.Create();
        var read = home.StartAsync();
        old.Cache.Clear();
        home.Rebind(current.Api, current.Session, CancellationToken.None);
        Assert.True(shared.IsCancellationRequested);
        await home.StartAsync();
        Assert.Null(home.Receipts.Failure);
        Assert.Null(home.Transfers.Failure);
        var people = old.Cache.People(old.Ledger);
        gate.SetResult();
        await read;
        Assert.Same(people, old.Cache.People(old.Ledger));
        Assert.Single(home.Receipts.Rows);
        Assert.Single(home.Transfers.Rows);
        Assert.Single(old.Paths);
        Assert.Equal(4, current.Paths.Count);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient http;
        public Guid Ledger { get; } = Guid.Parse("10000000-0000-4000-8000-000000000001");
        public Guid Person { get; } = Guid.NewGuid();
        private readonly Guid payment = Guid.Parse("10000000-0000-0000-0000-000000000000");
        public LedgerMasterCache Cache { get; } = new();
        public MonetailApiClient Api
        {
            get;
        }
        public ClientSession Session
        {
            get;
        }
        public PeopleInfo People => new([new(Person, "本人", null, null, 0, Guid.NewGuid())], null, "Admin", Guid.NewGuid());
        public Func<CancellationToken, Task>? BeforePeople
        {
            get; set;
        }
        public List<string> Paths { get; } = [];
        public List<string?> AccessTokens { get; } = [];
        public int Count(string suffix) => Paths.Count(path => path.EndsWith('/' + suffix, StringComparison.Ordinal));
        public Fixture(string access = "old-access")
        {
            var endpoint = ApiEndpoint.Parse("https://synthetic-api.example");
            http = new(new Handler(async (request, token) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                Paths.Add(path);
                AccessTokens.Add(request.Headers.Authorization?.Parameter);
                if (path.EndsWith("/people", StringComparison.Ordinal))
                {
                    if (BeforePeople is { } before)
                        await before(token);
                    return Json(People);
                }
                if (path.EndsWith("/payment-methods", StringComparison.Ordinal))
                    return Json(new PaymentMethodsInfo([new(payment, Person, "現金", 0, null, null, Guid.NewGuid())],
                        [new(Person, Guid.NewGuid())], null, "Admin"));
                if (path.EndsWith("/receipts", StringComparison.Ordinal))
                    return Json(new ReceiptListInfo(ReceiptListViewModelTests.Rows(0, 1), null));
                if (path.EndsWith("/transfers", StringComparison.Ordinal))
                    return Json(new TransferListInfo([new(Guid.NewGuid(), new(2026, 11, 10), "振替", payment, Guid.NewGuid(), 100, null)], null));
                throw new InvalidOperationException(path);
            }));
            Api = new(endpoint, http);
            Session = new(Api, endpoint, new Store(access));
        }
        public RecordHomeViewModel Create(CancellationToken token = default) => new(Ledger, new(2026, 11, 10), Cache, Api, Session, token);
        public void Dispose() => http.Dispose();
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
        private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => respond(request, token);
        }
        private sealed class Store(string access) : ITokenStore
        {
            private readonly TokenSet tokens = new(access, "refresh", DateTimeOffset.UtcNow.AddMinutes(10));
            public TokenSet? Load(ApiEndpoint endpoint) => tokens;
            public void Save(ApiEndpoint endpoint, TokenSet tokens) => throw new InvalidOperationException();
            public void Clear() => throw new InvalidOperationException();
        }
    }
}
