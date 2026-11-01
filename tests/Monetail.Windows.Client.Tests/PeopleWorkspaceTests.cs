using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Monetail.Windows.Client;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed class PeopleWorkspaceTests
{
    private static readonly Guid LedgerId = Guid.NewGuid();
    private static readonly PersonInfo Person = new(Guid.NewGuid(), "本人", new(2026, 4, 1), null, 0, Guid.NewGuid());
    private static readonly PeopleInfo People = new([Person], Person.PersonId, "Admin", Guid.NewGuid());

    [Fact]
    public async Task CreateRetryKeepsNormalizedInputAndRequestIdWithoutFetchingAgain()
    {
        var bodies = new List<string>();
        using var fixture = new Fixture(request =>
        {
            bodies.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return bodies.Count == 1 ? new(HttpStatusCode.ServiceUnavailable) : Json(People);
        });
        var draft = new PersonCreationDraft(new("  本人  ", new(2026, 4, 1), null));
        await Assert.ThrowsAsync<ApiConnectionException>(() => fixture.Workspace.CreateAsync(draft));
        Assert.Equal(Person, Assert.Single((await fixture.Workspace.CreateAsync(draft)).People));
        Assert.Equal(bodies[0], bodies[1]);
        using var document = JsonDocument.Parse(bodies[1]);
        Assert.Equal(draft.RequestId, document.RootElement.GetProperty("requestId").GetGuid());
        Assert.Equal("本人", document.RootElement.GetProperty("name").GetString());
        Assert.Equal("2026-04-01", document.RootElement.GetProperty("validFrom").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("validTo").ValueKind);
        Assert.Equal(2, fixture.Paths.Count);
        Assert.All(fixture.Paths, path => Assert.Equal((HttpMethod.Post, $"/ledgers/{LedgerId:N}/people"), path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NameOnlyUpdateOmitsProtectedPeriodWhileAdminCanExplicitlyClearIt(bool admin)
    {
        using var fixture = new Fixture(request =>
        {
            Assert.Equal(HttpMethod.Patch, request.Method);
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert.Equal(Person.Revision, body.RootElement.GetProperty("revision").GetGuid());
            Assert.Equal(admin, body.RootElement.TryGetProperty("validFrom", out var from));
            Assert.Equal(admin, body.RootElement.TryGetProperty("validTo", out _));
            if (admin) Assert.Equal(JsonValueKind.Null, from.ValueKind);
            return Json(People);
        });
        await fixture.Workspace.UpdateAsync(Person, new("新しい名前", null, null), admin);
        Assert.Single(fixture.Paths);
    }

    [Fact]
    public async Task DeleteSendsRevisionInBodyAndUsesReturnedList()
    {
        using var fixture = new Fixture(request =>
        {
            Assert.Equal(HttpMethod.Delete, request.Method);
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert.Equal(Person.Revision, body.RootElement.GetProperty("revision").GetGuid());
            return Json(new PeopleInfo([], null, "Admin", Guid.NewGuid()));
        });
        Assert.Empty((await fixture.Workspace.DeleteAsync(Person)).People);
        Assert.Single(fixture.Paths);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConflictsCarryTypedLatestWithoutAnotherRequest(bool order)
    {
        using var fixture = new Fixture(request => Json(new { code = "revision_conflict", field = (string?)null,
            message = "合成エラー", latest = order ? (object)People : Person }, HttpStatusCode.Conflict));
        var error = await Assert.ThrowsAsync<PeopleRequestException>(() => order
            ? fixture.Workspace.SaveOrderAsync(Guid.NewGuid(), [Person.PersonId])
            : fixture.Workspace.UpdateAsync(Person, new("入力を保持", null, null), false));
        Assert.Equal("revision_conflict", error.Code);
        if (order)
        {
            Assert.Equal(People.OrderRevision, error.LatestPeople!.OrderRevision);
            Assert.Equal(Person, Assert.Single(error.LatestPeople.People));
            Assert.Null(error.LatestPerson);
        }
        else { Assert.Equal(Person, error.LatestPerson); Assert.Null(error.LatestPeople); }
        Assert.Single(fixture.Paths);
        Assert.NotNull(fixture.Store.Current);
    }

    [Fact]
    public async Task OrderUsesCommonRevisionAndAllIdsInOnePut()
    {
        using var fixture = new Fixture(request =>
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.EndsWith("/people/order", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert.Equal(People.OrderRevision, body.RootElement.GetProperty("revision").GetGuid());
            Assert.Equal(Person.PersonId, body.RootElement.GetProperty("personIds")[0].GetGuid());
            return Json(People);
        });
        await fixture.Workspace.SaveOrderAsync(People.OrderRevision, [Person.PersonId]);
        Assert.Single(fixture.Paths);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task AuthorityLossAndLimitsDoNotClearLoginOrFetchLatest(HttpStatusCode status)
    {
        using var fixture = new Fixture(_ => new(status));
        var error = await Assert.ThrowsAsync<PeopleRequestException>(() => fixture.Workspace.ListAsync());
        Assert.Equal(status, error.StatusCode);
        Assert.Null(error.LatestPeople);
        Assert.NotNull(fixture.Store.Current);
        Assert.Single(fixture.Paths);
    }

    [Fact]
    public async Task FieldErrorIsSeparatedFromRevisionConflict()
    {
        using var fixture = new Fixture(_ => Json(new { code = "duplicate_person_name", field = "name", latest = (object?)null }, HttpStatusCode.Conflict));
        var error = await Assert.ThrowsAsync<PeopleRequestException>(() => fixture.Workspace.CreateAsync(new(new("本人", null, null))));
        Assert.Equal("name", error.Field);
        Assert.Null(error.LatestPerson);
        Assert.Null(error.LatestPeople);
    }

    [Fact]
    public async Task UnauthorizedRefreshUsesSameCreationRequestAndRotatedToken()
    {
        var bodies = new List<string>();
        using var fixture = new Fixture(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/connect/token")
                return Json(new { access_token = "updated", refresh_token = "rotated", token_type = "Bearer", expires_in = 900 });
            bodies.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return request.Headers.Authorization!.Parameter == "old" ? new(HttpStatusCode.Unauthorized) : Json(People);
        });
        await fixture.Workspace.CreateAsync(new(new("本人", null, null)));
        Assert.Equal(2, bodies.Count);
        Assert.Equal(bodies[0], bodies[1]);
        Assert.Equal("rotated", fixture.Store.Current!.RefreshToken);
        Assert.Equal(3, fixture.Paths.Count);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("missingSelf")]
    [InlineData("date")]
    [InlineData("order")]
    [InlineData("revision")]
    public async Task MalformedSuccessDoesNotBecomeAnEmptyOrUsableList(string kind)
    {
        var row = new { personId = Person.PersonId, name = Person.Name, validFrom = kind == "date" ? "2026-02-30" : "2026-04-01",
            validTo = (string?)null, displayOrder = kind == "order" ? (object)"0" : 0,
            revision = kind == "revision" ? Guid.Empty : Person.Revision };
        using var fixture = new Fixture(_ => Json(new { people = kind == "duplicate" ? new[] { row, row } : new[] { row },
            myPersonId = kind == "missingSelf" ? Guid.NewGuid() : Person.PersonId, currentRole = "Admin", orderRevision = Guid.NewGuid() }));
        await Assert.ThrowsAsync<ApiConnectionException>(() => fixture.Workspace.ListAsync());
        Assert.NotNull(fixture.Store.Current);
    }

    [Theory]
    [InlineData("", "name")]
    [InlineData("long", "name")]
    [InlineData("period", "validTo")]
    public void InvalidInputsAreRejectedBeforeSending(string kind, string field)
    {
        var error = Assert.Throws<PersonInputException>(() => new PersonInput(kind == "long" ? new string('あ', 101) : kind,
            kind == "period" ? new(2026, 10, 1) : null, kind == "period" ? new(2026, 9, 30) : null));
        Assert.Equal(field, error.Field);
    }

    private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(value) };
    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient http;
        public Store Store { get; } = new();
        public List<(HttpMethod, string)> Paths { get; } = [];
        public PeopleWorkspace Workspace { get; }
        public Fixture(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            var endpoint = ApiEndpoint.Parse("https://synthetic-api.example");
            http = new(new Handler(request =>
            {
                Assert.Equal("https", request.RequestUri!.Scheme);
                if (request.RequestUri.AbsolutePath != "/connect/token") Assert.NotNull(request.Headers.Authorization);
                Paths.Add((request.Method, request.RequestUri.AbsolutePath));
                return respond(request);
            }));
            var api = new MonetailApiClient(endpoint, http);
            Workspace = new(api, new(api, endpoint, Store), LedgerId);
        }
        public void Dispose() => http.Dispose();
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(respond(request));
    }
    public sealed class Store : ITokenStore
    {
        public TokenSet? Current { get; set; } = new("old", "refresh", DateTimeOffset.UtcNow.AddMinutes(10));
        public TokenSet? Load(ApiEndpoint endpoint) => Current;
        public void Save(ApiEndpoint endpoint, TokenSet tokens) => Current = tokens;
        public void Clear() => Current = null;
    }
}
