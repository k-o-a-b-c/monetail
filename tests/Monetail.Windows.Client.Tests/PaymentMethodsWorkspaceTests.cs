using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Monetail.Windows.Client;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed class PaymentMethodsWorkspaceTests
{
    private static readonly Guid Ledger = Guid.NewGuid(), Owner = Guid.NewGuid(), Other = Guid.NewGuid(), EmptyOwner = Guid.NewGuid();
    private static readonly PaymentMethodInfo Row = new(Guid.NewGuid(), Owner, "現金", 7, new(2026, 1, 1), new(2027, 12, 31), Guid.NewGuid());
    private static readonly PaymentMethodInfo Expired = new(Guid.NewGuid(), Owner, "終了済み", 7, null, new(2000, 1, 1), Guid.NewGuid());
    private static readonly PaymentMethodsInfo State = new([Expired, Row], [new(Other, Guid.NewGuid()), new(Owner, Guid.NewGuid()), new(EmptyOwner, Guid.NewGuid())], Owner, "Admin");
    private static object Payload(PaymentMethodsInfo? state = null, Guid? id = null)
    {
        state ??= State;
        return new { state.PaymentMethods, state.OwnerOrderRevisions, state.MyPersonId, state.CurrentRole, paymentMethodId = id ?? Row.PaymentMethodId };
    }
    private static PaymentMethodCreationDraft Draft() => new(Owner, new("  現金  ", new(2026, 1, 1), null));

    [Fact]
    public async Task ListPreservesAllDatesApiArrayOrderTiesGapsAndZeroPaymentOwners()
    {
        using var f = new Fixture(_ => Json(Payload())); var value = await f.Workspace.ListAsync();
        Assert.Equal(State.PaymentMethods, value.PaymentMethods); Assert.Equal(State.OwnerOrderRevisions, value.OwnerOrderRevisions);
        Assert.Equal(Owner, value.MyPersonId); Assert.Equal("Admin", value.CurrentRole);
        Assert.Single(f.Paths); Assert.Equal((HttpMethod.Get, $"/ledgers/{Ledger:N}/payment-methods"), f.Paths[0]);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CreationRetryAndAuthenticationRefreshKeepDraftIdAndExactBody(bool refresh)
    {
        var bodies = new List<string>();
        using var f = new Fixture(request => {
            if (request.RequestUri!.AbsolutePath == "/connect/token") return Json(new { access_token = "updated", refresh_token = "rotated", token_type = "Bearer", expires_in = 900 });
            Assert.Equal(HttpMethod.Post, request.Method);
            bodies.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return bodies.Count == 1 ? new(refresh ? HttpStatusCode.Unauthorized : HttpStatusCode.ServiceUnavailable) : Json(Payload());
        });
        var draft = Draft();
        if (!refresh) await Assert.ThrowsAsync<ApiConnectionException>(() => f.Workspace.CreateAsync(draft));
        var created = await f.Workspace.CreateAsync(draft);
        Assert.Equal(Row.PaymentMethodId, created.PaymentMethodId); Assert.Equal(State.PaymentMethods, created.State.PaymentMethods);
        Assert.Equal(bodies[0], bodies[1]); using var json = JsonDocument.Parse(bodies[1]);
        Assert.Equal(draft.RequestId, json.RootElement.GetProperty("requestId").GetGuid());
        Assert.Equal(Owner, json.RootElement.GetProperty("ownerPersonId").GetGuid()); Assert.Equal("現金", json.RootElement.GetProperty("name").GetString());
        Assert.Equal("2026-01-01", json.RootElement.GetProperty("validFrom").GetString()); Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("validTo").ValueKind);
        Assert.Equal(refresh ? 3 : 2, f.Paths.Count);
        if (refresh) Assert.Equal("rotated", f.Store.Current!.RefreshToken);
    }

    [Theory]
    [InlineData("name")] [InlineData("clearFrom")] [InlineData("setTo")] [InlineData("all")]
    public async Task PatchDistinguishesOmittedNameAndDatesFromExplicitNull(string kind)
    {
        var change = kind switch {
            "name" => new PaymentMethodChanges("  カード  "),
            "clearFrom" => new PaymentMethodChanges(validFrom: new(null)),
            "setTo" => new PaymentMethodChanges(validTo: new(new DateOnly(2028, 1, 1))),
            _ => new PaymentMethodChanges("  カード ", new(null), new(null))
        };
        using var f = new Fixture(request => {
            Assert.Equal(HttpMethod.Patch, request.Method); Assert.EndsWith($"/{Row.PaymentMethodId:N}", request.RequestUri!.AbsolutePath);
            using var json = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()); var body = json.RootElement;
            Assert.Equal(Row.Revision, body.GetProperty("revision").GetGuid());
            Assert.Equal(kind is "name" or "all", body.TryGetProperty("name", out var name));
            if (kind is "name" or "all") Assert.Equal("カード", name.GetString());
            Assert.Equal(kind is "clearFrom" or "all", body.TryGetProperty("validFrom", out var from));
            if (kind is "clearFrom" or "all") Assert.Equal(JsonValueKind.Null, from.ValueKind);
            Assert.Equal(kind is "setTo" or "all", body.TryGetProperty("validTo", out var to));
            if (kind == "setTo") Assert.Equal("2028-01-01", to.GetString());
            if (kind == "all") Assert.Equal(JsonValueKind.Null, to.ValueKind);
            Assert.False(body.TryGetProperty("ownerPersonId", out _)); Assert.False(body.TryGetProperty("displayOrder", out _));
            return Json(Payload());
        });
        await f.Workspace.UpdateAsync(Row, change); Assert.Single(f.Paths);
    }

    [Fact]
    public async Task DeleteUsesRowRevisionAndReturnedListWithoutGet()
    {
        using var f = new Fixture(request => {
            Assert.Equal(HttpMethod.Delete, request.Method); Assert.EndsWith($"/{Row.PaymentMethodId:N}", request.RequestUri!.AbsolutePath);
            using var json = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert.Equal(Row.Revision, json.RootElement.GetProperty("revision").GetGuid());
            return Json(Payload(State with { PaymentMethods = [] }));
        });
        Assert.Empty((await f.Workspace.DeleteAsync(Row)).PaymentMethods); Assert.Single(f.Paths);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task OrderSendsOneOwnerRevisionAndFullIdsWithoutDateFiltering(int count)
    {
        var ids = State.PaymentMethods.Take(count).Select(r => r.PaymentMethodId).ToArray(); var revision = State.OwnerOrderRevisions[1].OrderRevision;
        using var f = new Fixture(request => {
            Assert.Equal(HttpMethod.Put, request.Method); Assert.EndsWith("/payment-methods/order", request.RequestUri!.AbsolutePath);
            using var json = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert.Equal(Owner, json.RootElement.GetProperty("ownerPersonId").GetGuid()); Assert.Equal(revision, json.RootElement.GetProperty("revision").GetGuid());
            Assert.Equal(ids, json.RootElement.GetProperty("paymentMethodIds").EnumerateArray().Select(p => p.GetGuid())); return Json(Payload());
        });
        await f.Workspace.SaveOrderAsync(Owner, revision, ids); Assert.Single(f.Paths);
    }

    [Theory]
    [InlineData("create")] [InlineData("patch")] [InlineData("delete")] [InlineData("order")]
    public async Task RevisionConflictsHaveOperationSpecificTypedLatestAndNoAutomaticFetch(string operation)
    {
        var order = new PaymentMethodOrderInfo(Owner, Guid.NewGuid(), [Expired.PaymentMethodId, Row.PaymentMethodId], [Expired, Row]);
        object latest = operation == "create" ? Payload() : operation == "order" ? order : Row;
        using var f = new Fixture(_ => Json(new { code = "revision_conflict", latest }, HttpStatusCode.Conflict));
        var error = await Assert.ThrowsAsync<PaymentMethodRequestException>(() => Act(f, operation));
        if (operation == "create") { Assert.Equal(State.PaymentMethods, error.LatestPaymentMethods!.PaymentMethods); Assert.Null(error.LatestPaymentMethod); }
        else if (operation == "order") { Assert.Equal(order.OrderRevision, error.LatestOrder!.OrderRevision); Assert.Equal(order.PaymentMethodIds, error.LatestOrder.PaymentMethodIds); Assert.Equal(order.PaymentMethods, error.LatestOrder.PaymentMethods); }
        else Assert.Equal(Row, error.LatestPaymentMethod);
        Assert.Single(f.Paths); Assert.NotNull(f.Store.Current);
    }

    [Theory]
    [InlineData("duplicate_payment_method_name")] [InlineData("payment_method_referenced")]
    [InlineData("created_result_deleted")] [InlineData("request_conflict")]
    public async Task OtherConflictsPreserveCodesAndDoNotRequireLatest(string code)
    {
        using var f = new Fixture(_ => Json(new { code, field = "name", message = "表示しないサーバー本文" }, HttpStatusCode.Conflict));
        var error = await Assert.ThrowsAsync<PaymentMethodRequestException>(() => f.Workspace.CreateAsync(Draft()));
        Assert.Equal(code, error.Code); Assert.Equal("name", error.Field); Assert.Null(error.LatestPaymentMethod); Assert.Null(error.LatestPaymentMethods); Assert.Null(error.LatestOrder);
        Assert.DoesNotContain("サーバー本文", error.Message); Assert.Single(f.Paths);
    }

    [Theory]
    [InlineData("Admin", false, true)] [InlineData("Editor", true, true)] [InlineData("Editor", false, false)]
    [InlineData("Viewer", true, false)] [InlineData("Admin", true, true)]
    public void PermissionAvailabilityFollowsCurrentRoleAndOwnerMapping(string role, bool mapped, bool allowed)
    {
        var info = State with { CurrentRole = role, MyPersonId = mapped ? Owner : null };
        Assert.Equal(allowed, info.CanManageOwner(Owner)); Assert.Equal(role == "Admin", info.CanManageOwner(Other));
        Assert.False(info.CanManageOwner(Guid.NewGuid()));
    }

    [Theory]
    [InlineData(403, "Viewer")] [InlineData(403, "Editor")] [InlineData(404, null)] [InlineData(429, null)]
    public async Task AuthorityLossAndLimitsRetainLoginAndExposePermissionsWithoutGet(int status, string? role)
    {
        using var f = new Fixture(_ => status == 429 ? new((HttpStatusCode)status) : Json(new { code = "forbidden", permissions = new { currentRole = role, myPersonId = (Guid?)null } }, (HttpStatusCode)status));
        var error = await Assert.ThrowsAsync<PaymentMethodRequestException>(() => f.Workspace.UpdateAsync(Row, new("新しい名前")));
        Assert.Equal((HttpStatusCode)status, error.StatusCode);
        if (status != 429) Assert.Equal(role, error.Permissions!.CurrentRole);
        Assert.NotNull(f.Store.Current); Assert.Single(f.Paths);
    }

    [Fact]
    public async Task RefreshFailureClearsOnlyRejectedAuthentication()
    {
        using var f = new Fixture(request => request.RequestUri!.AbsolutePath == "/connect/token"
            ? Json(new { error = "invalid_grant" }, HttpStatusCode.BadRequest) : new(HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<AuthenticationRejectedException>(() => f.Workspace.ListAsync()); Assert.Null(f.Store.Current); Assert.Equal(2, f.Paths.Count);
    }

    [Fact]
    public async Task ExternalLoginChangeDoesNotCarryDraftIntoAnotherAccount()
    {
        using var f = new Fixture(_ => Json(Payload())); await f.Workspace.ListAsync();
        f.Store.Current = new("another", "another-refresh", DateTimeOffset.UtcNow.AddMinutes(10));
        await Assert.ThrowsAsync<AuthenticationContextChangedException>(() => f.Workspace.CreateAsync(Draft())); Assert.Single(f.Paths);
        Assert.Equal("another", f.Store.Current.AccessToken);
    }

    [Theory]
    [InlineData("name")] [InlineData("long")] [InlineData("emoji")] [InlineData("period")] [InlineData("patchPeriod")]
    [InlineData("emptyPatch")] [InlineData("owner")] [InlineData("revision")] [InlineData("duplicateIds")] [InlineData("emptyId")]
    public async Task InvalidInputDoesNotSendHttp(string kind)
    {
        using var f = new Fixture(_ => throw new InvalidOperationException("送信不可"));
        await Assert.ThrowsAsync<PaymentMethodInputException>(async () => {
            switch (kind)
            {
                case "name": await f.Workspace.CreateAsync(new(Owner, new("　 "))); break;
                case "long": await f.Workspace.CreateAsync(new(Owner, new(new string('あ', 101)))); break;
                case "emoji": await f.Workspace.CreateAsync(new(Owner, new(string.Concat(Enumerable.Repeat("😀", 51))))); break;
                case "period": await f.Workspace.CreateAsync(new(Owner, new("現金", new(2027, 1, 1), new(2026, 1, 1)))); break;
                case "patchPeriod": await f.Workspace.UpdateAsync(Row, new(validTo: new(new DateOnly(2020, 1, 1)))); break;
                case "emptyPatch": await f.Workspace.UpdateAsync(Row, new()); break;
                case "owner": await f.Workspace.CreateAsync(new(Guid.Empty, new("現金"))); break;
                case "revision": await f.Workspace.DeleteAsync(Row with { Revision = Guid.Empty }); break;
                case "duplicateIds": await f.Workspace.SaveOrderAsync(Owner, Guid.NewGuid(), [Row.PaymentMethodId, Row.PaymentMethodId]); break;
                default: await f.Workspace.SaveOrderAsync(Owner, Guid.NewGuid(), [Guid.Empty]); break;
            }
        });
        Assert.Empty(f.Paths);
    }

    [Fact]
    public void Utf16LimitAllows100UnitsAnd50Emoji()
    {
        Assert.Equal(100, new PaymentMethodInput(new string('あ', 100)).Name.Length);
        Assert.Equal(100, new PaymentMethodInput(string.Concat(Enumerable.Repeat("😀", 50))).Name.Length);
    }

    [Theory]
    [InlineData("duplicatePayment")] [InlineData("duplicateOwner")] [InlineData("missingOwner")]
    [InlineData("unknownSelf")] [InlineData("role")] [InlineData("date")] [InlineData("period")]
    [InlineData("revision")] [InlineData("order")] [InlineData("ownerShape")]
    [InlineData("missingPeriod")] [InlineData("emptyName")]
    public async Task MalformedListsNeverBecomeUsableOrEmptyState(string kind)
    {
        var payload = JsonSerializer.SerializeToNode(Payload(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var rows = payload["paymentMethods"]!.AsArray(); var owners = payload["ownerOrderRevisions"]!.AsArray();
        switch (kind)
        {
            case "duplicatePayment": rows.Add(rows[0]!.DeepClone()); break;
            case "duplicateOwner": owners.Add(owners[0]!.DeepClone()); break;
            case "missingOwner": rows[0]!["ownerPersonId"] = Guid.NewGuid().ToString(); break;
            case "unknownSelf": payload["myPersonId"] = Guid.NewGuid().ToString(); break;
            case "role": payload["currentRole"] = "Owner"; break;
            case "date": rows[0]!["validFrom"] = "2026-02-30"; break;
            case "period": rows[0]!["validFrom"] = "2026-01-01"; break;
            case "revision": owners[0]!["orderRevision"] = Guid.Empty.ToString(); break;
            case "order": rows[0]!["displayOrder"] = -1; break;
            case "ownerShape": owners[0] = null; break;
            case "missingPeriod": rows[0]!.AsObject().Remove("validTo"); break;
            default: rows[0]!["name"] = " "; break;
        }
        using var f = new Fixture(_ => new(HttpStatusCode.OK) { Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json") });
        await Assert.ThrowsAsync<ApiConnectionException>(() => f.Workspace.ListAsync()); Assert.NotNull(f.Store.Current); Assert.Single(f.Paths);
    }

    [Theory]
    [InlineData("missing")] [InlineData("wrongRow")] [InlineData("wrongOwner")] [InlineData("duplicateIds")]
    [InlineData("badOrderRevision")] [InlineData("badOrderId")]
    [InlineData("wrongRowOwner")] [InlineData("missingRows")] [InlineData("mismatchedRows")]
    public async Task MalformedOrMisdirectedConflictsDoNotReplaceInput(string kind)
    {
        var order = kind is "wrongOwner" or "duplicateIds" or "badOrderRevision" or "badOrderId" or "wrongRowOwner" or "missingRows" or "mismatchedRows";
        object? latest = kind switch {
            "wrongRowOwner" => new PaymentMethodOrderInfo(Owner, Guid.NewGuid(), [Row.PaymentMethodId], [Row with { OwnerPersonId = Other }]),
            "missingRows" => new { ownerPersonId = Owner, orderRevision = Guid.NewGuid(), paymentMethodIds = new[] { Row.PaymentMethodId } },
            "mismatchedRows" => new PaymentMethodOrderInfo(Owner, Guid.NewGuid(), [Row.PaymentMethodId], [Expired]),
            "wrongRow" => Row with { PaymentMethodId = Guid.NewGuid() },
            "wrongOwner" => new PaymentMethodOrderInfo(Other, Guid.NewGuid(), [Row.PaymentMethodId], [Row]),
            "duplicateIds" => new PaymentMethodOrderInfo(Owner, Guid.NewGuid(), [Row.PaymentMethodId, Row.PaymentMethodId], [Row, Row]),
            "badOrderRevision" => new PaymentMethodOrderInfo(Owner, Guid.Empty, [], []),
            "badOrderId" => new PaymentMethodOrderInfo(Owner, Guid.NewGuid(), [Guid.Empty], []), _ => null
        };
        using var f = new Fixture(_ => Json(new { code = "revision_conflict", latest }, HttpStatusCode.Conflict));
        await Assert.ThrowsAsync<ApiConnectionException>(() => Act(f, order ? "order" : "patch")); Assert.NotNull(f.Store.Current); Assert.Single(f.Paths);
    }

    [Fact]
    public async Task CreationMustReturnItsIdAndOwnerInTheReturnedCollection()
    {
        using var f = new Fixture(_ => Json(Payload(id: Guid.NewGuid())));
        await Assert.ThrowsAsync<ApiConnectionException>(() => f.Workspace.CreateAsync(Draft())); Assert.NotNull(f.Store.Current);
    }

    [Theory]
    [InlineData("{\"currentRole\":\"Owner\",\"myPersonId\":null}")]
    [InlineData("{\"currentRole\":null,\"myPersonId\":\"7c46bcc2-bd09-4a35-a458-7e83848c35d0\"}")]
    public async Task InvalidPermissionContextDoesNotGrantCapabilities(string permissions)
    {
        using var f = new Fixture(_ => new(HttpStatusCode.Forbidden) { Content = new StringContent("{\"permissions\":" + permissions + "}", Encoding.UTF8, "application/json") });
        await Assert.ThrowsAsync<ApiConnectionException>(() => f.Workspace.ListAsync()); Assert.NotNull(f.Store.Current);
    }

    [Fact]
    public async Task PreCancelledOperationDoesNotSendOrClearAuthentication()
    {
        using var f = new Fixture(_ => Json(Payload())); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Workspace.ListAsync(cancellation.Token));
        Assert.Empty(f.Paths); Assert.NotNull(f.Store.Current);
    }

    [Theory]
    [InlineData("get")] [InlineData("patch")] [InlineData("delete")] [InlineData("order")]
    public async Task AuthenticationRefreshWorksForEveryOtherOperation(string operation)
    {
        var bodies = new List<string?>();
        using var f = new Fixture(request => {
            if (request.RequestUri!.AbsolutePath == "/connect/token") return Json(new { access_token = "updated", refresh_token = "rotated", token_type = "Bearer", expires_in = 900 });
            bodies.Add(request.Content?.ReadAsStringAsync().GetAwaiter().GetResult());
            return request.Headers.Authorization!.Parameter == "old" ? new(HttpStatusCode.Unauthorized) : Json(Payload());
        });
        if (operation == "get") await f.Workspace.ListAsync(); else await Act(f, operation);
        Assert.Equal(2, bodies.Count); Assert.Equal(bodies[0], bodies[1]); Assert.Equal("rotated", f.Store.Current!.RefreshToken);
    }

    private static Task Act(Fixture f, string operation) => operation switch {
        "create" => f.Workspace.CreateAsync(Draft()), "patch" => f.Workspace.UpdateAsync(Row, new("変更")),
        "delete" => f.Workspace.DeleteAsync(Row), _ => f.Workspace.SaveOrderAsync(Owner, Guid.NewGuid(), [Row.PaymentMethodId])
    };
    private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(value) };
    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient http;
        public PeopleWorkspaceTests.Store Store { get; } = new();
        public List<(HttpMethod, string)> Paths { get; } = [];
        public PaymentMethodsWorkspace Workspace { get; }
        public Fixture(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            var endpoint = ApiEndpoint.Parse("https://synthetic-api.example");
            http = new(new Handler(request => {
                Assert.Equal("https", request.RequestUri!.Scheme);
                if (request.RequestUri.AbsolutePath != "/connect/token") Assert.NotNull(request.Headers.Authorization);
                Paths.Add((request.Method, request.RequestUri.AbsolutePath)); return respond(request);
            }));
            var api = new MonetailApiClient(endpoint, http); Workspace = new(api, new(api, endpoint, Store), Ledger);
        }
        public void Dispose() => http.Dispose();
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(respond(request));
    }
}
