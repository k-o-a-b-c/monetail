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

public sealed class CategoriesWorkspaceTests
{
    private static readonly Guid Ledger = Guid.NewGuid();
    private static readonly CategoryInfo Row = new(Guid.NewGuid(), "食費", "Expense", "ReducedFood", true, 7, Guid.NewGuid());
    private static readonly CategoryInfo Disabled = new(Guid.NewGuid(), "給与", "Income", "Exempt", false, 7, Guid.NewGuid());
    private static readonly CategoriesInfo State = new([Disabled, Row], Guid.NewGuid(), "Admin");
    private static object Payload(CategoriesInfo? state = null, Guid? id = null)
    {
        state ??= State; return new { state.Categories, state.OrderRevision, state.CurrentRole, categoryId = id ?? Row.CategoryId };
    }
    private static CategoryCreationDraft Draft() => new(new("　食費　", "Expense", "ReducedFood"));

    [Fact]
    public async Task ListKeepsApiOrderDisabledRowsTiesAndGapsWithoutExtraGet()
    {
        using var f = new Fixture(_ => Json(Payload())); var state = await f.Workspace.ListAsync();
        Assert.Equal(State.Categories, state.Categories); Assert.Equal(State.OrderRevision, state.OrderRevision); Assert.Equal("Admin", state.CurrentRole);
        Assert.Single(f.Paths); Assert.Equal((HttpMethod.Get, $"/ledgers/{Ledger:N}/categories"), f.Paths[0]);
    }

    [Theory]
    [InlineData("Income", "Standard")] [InlineData("Expense", "ReducedFood")]
    [InlineData("Expense", "ReducedNewspaper")] [InlineData("Income", "Exempt")]
    public async Task CreateSendsOnlyConfirmedCodesAndReturnsIdWithUpdatedList(string income, string tax)
    {
        using var f = new Fixture(request => {
            Assert.Equal(HttpMethod.Post, request.Method); var body = Body(request);
            Assert.Equal(4, body.EnumerateObject().Count()); Assert.Equal("食費", body.GetProperty("name").GetString());
            Assert.Equal(income, body.GetProperty("incomeExpense").GetString()); Assert.Equal(tax, body.GetProperty("taxCategoryCode").GetString());
            return Json(Payload(State with { Categories = [Row with { IncomeExpense = income, TaxCategoryCode = tax }] }), HttpStatusCode.Created);
        });
        var created = await f.Workspace.CreateAsync(new(new(" 食費 ", income, tax)));
        Assert.Equal(Row.CategoryId, created.CategoryId); Assert.Equal(tax, Assert.Single(created.State.Categories).TaxCategoryCode); Assert.Single(f.Paths);
    }

    [Theory]
    [InlineData("network")] [InlineData("store")] [InlineData("refresh")]
    public async Task CreationRetryKeepsRequestIdAndNormalizedInput(string failure)
    {
        var bodies = new List<string>();
        using var f = new Fixture(request => {
            if (request.RequestUri!.AbsolutePath == "/connect/token") return Refreshed();
            bodies.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            if (bodies.Count == 1)
            {
                if (failure == "network") throw new HttpRequestException("synthetic");
                return failure == "refresh" ? new(HttpStatusCode.Unauthorized) : Json(new { code = "store_unavailable" }, HttpStatusCode.ServiceUnavailable);
            }
            return Json(Payload());
        });
        var draft = Draft();
        if (failure == "network") await Assert.ThrowsAsync<ApiConnectionException>(() => f.Workspace.CreateAsync(draft));
        if (failure == "store") await Assert.ThrowsAsync<CategoryRequestException>(() => f.Workspace.CreateAsync(draft));
        var created = await f.Workspace.CreateAsync(draft);
        Assert.Equal(Row.CategoryId, created.CategoryId); Assert.Equal(bodies[0], bodies[1]);
        using var json = JsonDocument.Parse(bodies[1]); Assert.Equal(draft.RequestId, json.RootElement.GetProperty("requestId").GetGuid());
        Assert.Equal("食費", json.RootElement.GetProperty("name").GetString()); Assert.Equal(failure == "refresh" ? 3 : 2, f.Paths.Count);
        Assert.NotEqual(draft.RequestId, Draft().RequestId);
    }

    [Theory]
    [InlineData("name")] [InlineData("tax")] [InlineData("disable")] [InlineData("enable")] [InlineData("all")]
    public async Task PatchSendsOnlySpecifiedMutableFieldsAndRowRevision(string kind)
    {
        var change = kind switch { "name" => new CategoryChanges("　生活費 "), "tax" => new(taxCategoryCode: "Standard"),
            "disable" => new(isEnabled: false), "enable" => new(isEnabled: true), _ => new("生活費", "Standard", false) };
        using var f = new Fixture(request => {
            Assert.Equal(HttpMethod.Patch, request.Method); Assert.EndsWith($"/{Row.CategoryId:N}", request.RequestUri!.AbsolutePath); var body = Body(request);
            Assert.Equal(Row.Revision, body.GetProperty("revision").GetGuid());
            Assert.Equal(kind is "name" or "all", body.TryGetProperty("name", out var name)); if (kind is "name" or "all") Assert.Equal("生活費", name.GetString());
            Assert.Equal(kind is "tax" or "all", body.TryGetProperty("taxCategoryCode", out var tax)); if (kind is "tax" or "all") Assert.Equal("Standard", tax.GetString());
            Assert.Equal(kind is "disable" or "enable" or "all", body.TryGetProperty("isEnabled", out var enabled)); if (kind is "disable" or "enable" or "all") Assert.Equal(kind == "enable", enabled.GetBoolean());
            Assert.DoesNotContain(body.EnumerateObject(), p => p.Name is "incomeExpense" or "displayOrder" or "categoryId" or "orderRevision");
            return Json(Payload());
        });
        Assert.Equal(State.Categories, (await f.Workspace.UpdateAsync(Row, change)).Categories); Assert.Single(f.Paths);
        Assert.Equal("食費", Row.Name); Assert.True(Row.IsEnabled);
    }

    [Fact]
    public async Task DeleteSendsOnlyRowRevisionAndUsesReturnedList()
    {
        using var f = new Fixture(request => {
            Assert.Equal(HttpMethod.Delete, request.Method); Assert.EndsWith($"/{Row.CategoryId:N}", request.RequestUri!.AbsolutePath);
            var body = Body(request); Assert.Single(body.EnumerateObject()); Assert.Equal(Row.Revision, body.GetProperty("revision").GetGuid());
            return Json(Payload(State with { Categories = [] }));
        });
        Assert.Empty((await f.Workspace.DeleteAsync(Row)).Categories); Assert.Single(f.Paths);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task OrderSendsCollectionRevisionAndFullArrayIncludingDisabled(int count)
    {
        var ids = State.Categories.Take(count).Select(r => r.CategoryId).ToArray();
        using var f = new Fixture(request => {
            Assert.Equal(HttpMethod.Put, request.Method); Assert.EndsWith("/categories/order", request.RequestUri!.AbsolutePath); var body = Body(request);
            Assert.Equal(2, body.EnumerateObject().Count()); Assert.Equal(State.OrderRevision, body.GetProperty("revision").GetGuid());
            Assert.Equal(ids, body.GetProperty("categoryIds").EnumerateArray().Select(r => r.GetGuid())); return Json(Payload(State with { Categories = State.Categories.Take(count).ToArray() }));
        });
        Assert.Equal(count, (await f.Workspace.SaveOrderAsync(State.OrderRevision, ids)).Categories.Length); Assert.Single(f.Paths);
    }

    [Theory]
    [InlineData("Admin", true)] [InlineData("Editor", false)] [InlineData("Viewer", false)]
    public async Task PermissionCapabilityUsesOnlyLedgerRole(string role, bool expected)
    {
        using var f = new Fixture(_ => Json(Payload(State with { CurrentRole = role })));
        Assert.Equal(expected, (await f.Workspace.ListAsync()).CanManage);
    }

    [Theory]
    [InlineData("create", true)] [InlineData("patch", false)] [InlineData("delete", false)]
    [InlineData("delete", true)] [InlineData("order", true)]
    public async Task ConflictReadsLatestRowOrCollectionWithoutApplyingOrFetching(string operation, bool collection)
    {
        var row = Row with { Name = "最新名称", IsEnabled = false, Revision = Guid.NewGuid() };
        var state = State with { OrderRevision = Guid.NewGuid(), Categories = [Disabled, row, Row with { CategoryId = Guid.NewGuid(), Name = "追加" }] };
        using var f = new Fixture(_ => Json(new { code = "revision_conflict", latest = collection ? (object)Payload(state) : row }, HttpStatusCode.Conflict));
        var error = await Assert.ThrowsAsync<CategoryRequestException>(() => Act(f, operation));
        Assert.Equal("revision_conflict", error.Code);
        if (collection) { Assert.Equal(state.Categories, error.LatestCategories!.Categories); Assert.Equal(state.OrderRevision, error.LatestCategories.OrderRevision); Assert.Null(error.LatestCategory); }
        else { Assert.Equal(row, error.LatestCategory); Assert.Null(error.LatestCategories); }
        Assert.Equal("食費", Row.Name); Assert.NotEqual(state.OrderRevision, State.OrderRevision); Assert.Single(f.Paths); Assert.NotNull(f.Store.Current);
    }

    [Theory]
    [InlineData(400, "invalid_input")] [InlineData(409, "duplicate_category_name")] [InlineData(409, "category_referenced")]
    [InlineData(409, "request_conflict")] [InlineData(409, "created_result_deleted")] [InlineData(503, "store_unavailable")]
    public async Task BusinessErrorsKeepCodeAndFieldWithoutReadingLatestOrFetching(int status, string code)
    {
        using var f = new Fixture(_ => Json(new { code, field = "name", message = "表示しないサーバー本文", latest = "不正でも参照しない" }, (HttpStatusCode)status));
        var error = await Assert.ThrowsAsync<CategoryRequestException>(() => f.Workspace.CreateAsync(Draft()));
        Assert.Equal((HttpStatusCode)status, error.StatusCode); Assert.Equal(code, error.Code); Assert.Equal("name", error.Field);
        Assert.Null(error.LatestCategory); Assert.Null(error.LatestCategories); Assert.DoesNotContain("サーバー本文", error.Message); Assert.Single(f.Paths); Assert.NotNull(f.Store.Current);
    }

    [Theory]
    [InlineData(403, "Viewer")] [InlineData(403, "Editor")] [InlineData(404, null)]
    public async Task PermissionLossRetainsCurrentPermissionAndIgnoresPrivateLatest(int status, string? role)
    {
        Guid? mine = role is null ? null : Guid.NewGuid();
        using var f = new Fixture(_ => Json(new { code = "forbidden", permissions = new { currentRole = role, myPersonId = mine }, latest = "公開しない" }, (HttpStatusCode)status));
        var error = await Assert.ThrowsAsync<CategoryRequestException>(() => f.Workspace.UpdateAsync(Row, new("未保存")));
        Assert.Equal(role, error.Permissions!.CurrentRole); Assert.Equal(mine, error.Permissions.MyPersonId);
        Assert.Null(error.LatestCategory); Assert.Null(error.LatestCategories); Assert.Single(f.Paths); Assert.NotNull(f.Store.Current);
    }

    [Theory]
    [InlineData(403)] [InlineData(404)] [InlineData(429)] [InlineData(503)]
    public async Task EmptyKnownErrorResponsesRemainTypedWithoutAuthenticationLoss(int status)
    {
        using var f = new Fixture(_ => new((HttpStatusCode)status));
        var error = await Assert.ThrowsAsync<CategoryRequestException>(() => f.Workspace.ListAsync());
        Assert.Equal((HttpStatusCode)status, error.StatusCode); Assert.Null(error.Permissions); Assert.Single(f.Paths); Assert.NotNull(f.Store.Current);
    }

    [Theory]
    [InlineData("blank")] [InlineData("long")] [InlineData("emoji")] [InlineData("income")] [InlineData("incomeCase")]
    [InlineData("tax")] [InlineData("taxCase")] [InlineData("emptyPatch")] [InlineData("patchTax")]
    [InlineData("ledger")] [InlineData("row")] [InlineData("rowRevision")] [InlineData("orderRevision")] [InlineData("duplicateIds")] [InlineData("emptyId")]
    public async Task InvalidInputNeverSendsHttp(string kind)
    {
        using var f = new Fixture(_ => throw new InvalidOperationException("送信不可"));
        var error = await Assert.ThrowsAsync<CategoryInputException>(async () => {
            switch (kind)
            {
                case "blank": await f.Workspace.CreateAsync(new(new("　\t", "Income", "Standard"))); break;
                case "long": await f.Workspace.CreateAsync(new(new(new string('あ', 101), "Income", "Standard"))); break;
                case "emoji": await f.Workspace.CreateAsync(new(new(string.Concat(Enumerable.Repeat("😀", 51)), "Income", "Standard"))); break;
                case "income": await f.Workspace.CreateAsync(new(new("科目", "Other", "Standard"))); break;
                case "incomeCase": await f.Workspace.CreateAsync(new(new("科目", "income", "Standard"))); break;
                case "tax": await f.Workspace.CreateAsync(new(new("科目", "Income", "Other"))); break;
                case "taxCase": await f.Workspace.CreateAsync(new(new("科目", "Income", "standard"))); break;
                case "emptyPatch": await f.Workspace.UpdateAsync(Row, new()); break;
                case "patchTax": await f.Workspace.UpdateAsync(Row, new(taxCategoryCode: "Other")); break;
                case "ledger": await f.Api.ListCategoriesAsync("old", Guid.Empty); break;
                case "row": await f.Workspace.DeleteAsync(Row with { CategoryId = Guid.Empty }); break;
                case "rowRevision": await f.Workspace.UpdateAsync(Row with { Revision = Guid.Empty }, new("変更")); break;
                case "orderRevision": await f.Workspace.SaveOrderAsync(Guid.Empty, []); break;
                case "duplicateIds": await f.Workspace.SaveOrderAsync(State.OrderRevision, [Row.CategoryId, Row.CategoryId]); break;
                default: await f.Workspace.SaveOrderAsync(State.OrderRevision, [Guid.Empty]); break;
            }
        });
        Assert.False(string.IsNullOrEmpty(error.Field)); Assert.Empty(f.Paths);
    }

    [Fact]
    public void Utf16NameBoundaryIsAllowedWithoutTruncation()
    {
        Assert.Equal(100, new CategoryInput(" " + new string('あ', 100) + " ", "Income", "Standard").Name.Length);
        Assert.Equal(100, new CategoryChanges(string.Concat(Enumerable.Repeat("😀", 50))).Name!.Length);
    }

    [Theory]
    [InlineData("missingRows")] [InlineData("nullRows")] [InlineData("rowsObject")] [InlineData("duplicateId")] [InlineData("nullRow")]
    [InlineData("id")] [InlineData("rowRevision")] [InlineData("orderRevision")] [InlineData("missingRevision")] [InlineData("role")] [InlineData("missingRole")]
    [InlineData("name")] [InlineData("longName")] [InlineData("income")] [InlineData("missingIncome")] [InlineData("tax")] [InlineData("missingTax")]
    [InlineData("enabled")] [InlineData("nullEnabled")] [InlineData("missingEnabled")] [InlineData("order")] [InlineData("fractionalOrder")] [InlineData("missingOrder")]
    [InlineData("json")] [InlineData("array")]
    public async Task MalformedResponseNeverBecomesUsableState(string kind)
    {
        var payload = JsonSerializer.SerializeToNode(Payload(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!; var rows = payload["categories"]!.AsArray();
        switch (kind)
        {
            case "missingRows": payload.AsObject().Remove("categories"); break;
            case "nullRows": payload["categories"] = null; break;
            case "rowsObject": payload["categories"] = new System.Text.Json.Nodes.JsonObject(); break;
            case "duplicateId": rows.Add(rows[0]!.DeepClone()); break;
            case "nullRow": rows[0] = null; break;
            case "id": rows[0]!["categoryId"] = "invalid"; break;
            case "rowRevision": rows[0]!["revision"] = Guid.Empty.ToString(); break;
            case "orderRevision": payload["orderRevision"] = Guid.Empty.ToString(); break;
            case "missingRevision": payload.AsObject().Remove("orderRevision"); break;
            case "role": payload["currentRole"] = "Owner"; break;
            case "missingRole": payload.AsObject().Remove("currentRole"); break;
            case "name": rows[0]!["name"] = " "; break;
            case "longName": rows[0]!["name"] = new string('あ', 101); break;
            case "income": rows[0]!["incomeExpense"] = "income"; break;
            case "missingIncome": rows[0]!.AsObject().Remove("incomeExpense"); break;
            case "tax": rows[0]!["taxCategoryCode"] = "Other"; break;
            case "missingTax": rows[0]!.AsObject().Remove("taxCategoryCode"); break;
            case "enabled": rows[0]!["isEnabled"] = 1; break;
            case "nullEnabled": rows[0]!["isEnabled"] = null; break;
            case "missingEnabled": rows[0]!.AsObject().Remove("isEnabled"); break;
            case "order": rows[0]!["displayOrder"] = -1; break;
            case "fractionalOrder": rows[0]!["displayOrder"] = 1.5; break;
            case "missingOrder": rows[0]!.AsObject().Remove("displayOrder"); break;
        }
        using var f = new Fixture(_ => new(HttpStatusCode.OK) { Content = new StringContent(kind == "json" ? "{" : kind == "array" ? "[]" : payload.ToJsonString(), Encoding.UTF8, "application/json") });
        await Assert.ThrowsAsync<ApiConnectionException>(() => f.Workspace.ListAsync()); Assert.Single(f.Paths); Assert.NotNull(f.Store.Current);
    }

    [Theory]
    [InlineData("missing")] [InlineData("null")] [InlineData("wrongId")] [InlineData("wrongShape")]
    [InlineData("rowForOrder")] [InlineData("badCollection")]
    public async Task MalformedOrMisdirectedLatestIsRejected(string kind)
    {
        object? latest = kind switch { "wrongId" => Row with { CategoryId = Guid.NewGuid() }, "wrongShape" => new { revision = Row.Revision },
            "rowForOrder" => Row, "badCollection" => State with { OrderRevision = Guid.Empty }, _ => null };
        using var f = new Fixture(_ => Json(kind == "missing" ? new { code = "revision_conflict" } : (object)new { code = "revision_conflict", latest }, HttpStatusCode.Conflict));
        await Assert.ThrowsAsync<ApiConnectionException>(() => Act(f, kind == "rowForOrder" ? "order" : "patch")); Assert.Single(f.Paths); Assert.NotNull(f.Store.Current);
    }

    [Fact]
    public async Task CreateRejectsResultIdNotInReturnedList()
    {
        using var f = new Fixture(_ => Json(Payload(id: Guid.NewGuid())));
        await Assert.ThrowsAsync<ApiConnectionException>(() => f.Workspace.CreateAsync(Draft())); Assert.Single(f.Paths);
    }

    [Theory]
    [InlineData("{\"currentRole\":\"Owner\",\"myPersonId\":null}")]
    [InlineData("{\"currentRole\":null,\"myPersonId\":\"7c46bcc2-bd09-4a35-a458-7e83848c35d0\"}")]
    public async Task MalformedPermissionsNeverGrantCapabilities(string permissions)
    {
        using var f = new Fixture(_ => new(HttpStatusCode.Forbidden) { Content = new StringContent("{\"permissions\":" + permissions + "}", Encoding.UTF8, "application/json") });
        await Assert.ThrowsAsync<ApiConnectionException>(() => f.Workspace.ListAsync()); Assert.NotNull(f.Store.Current);
    }

    [Theory]
    [InlineData("get")] [InlineData("create")] [InlineData("patch")] [InlineData("delete")] [InlineData("order")]
    public async Task AuthenticationRefreshKeepsEveryOperationBodyAndVersion(string operation)
    {
        var bodies = new List<string?>();
        using var f = new Fixture(request => {
            if (request.RequestUri!.AbsolutePath == "/connect/token") return Refreshed();
            bodies.Add(request.Content?.ReadAsStringAsync().GetAwaiter().GetResult());
            return request.Headers.Authorization!.Parameter == "old" ? new(HttpStatusCode.Unauthorized) : Json(Payload());
        });
        var draft = Draft(); await Act(f, operation, draft);
        Assert.Equal(2, bodies.Count); Assert.Equal(bodies[0], bodies[1]); Assert.Equal(3, f.Paths.Count); Assert.Equal("rotated", f.Store.Current!.RefreshToken);
    }

    [Fact]
    public async Task OrderCopiesCallerArrayBeforeWaitingForAuthenticationRefresh()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var f = new Fixture(async (request, token) => {
            if (request.RequestUri!.AbsolutePath == "/connect/token") { entered.SetResult(); await release.Task.WaitAsync(token); return Refreshed(); }
            Assert.Equal(new[] { Disabled.CategoryId, Row.CategoryId }, Body(request).GetProperty("categoryIds").EnumerateArray().Select(p => p.GetGuid())); return Json(Payload());
        });
        f.Store.Current = new("old", "refresh", DateTimeOffset.UtcNow.AddSeconds(-1));
        var ids = new[] { Disabled.CategoryId, Row.CategoryId }; var task = f.Workspace.SaveOrderAsync(State.OrderRevision, ids);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); ids[0] = Guid.NewGuid(); release.SetResult(); await task;
        Assert.Equal(2, f.Paths.Count);
    }

    [Fact]
    public async Task RejectedLoginNeverParsesPrivateLatestAndExplicitSameAccountLoginCanReuseDraft()
    {
        var authenticated = false; var user = Guid.NewGuid(); var bodies = new List<string>();
        using var f = new Fixture(request => {
            if (request.RequestUri!.AbsolutePath == "/connect/token") return Json(new { error = "invalid_grant" }, HttpStatusCode.BadRequest);
            if (request.RequestUri.AbsolutePath == "/auth/me") return Json(new { authUserId = user, displayName = "本人", expiresAtUtc = DateTimeOffset.UtcNow.AddDays(1), verifiedAtUtc = DateTimeOffset.UtcNow });
            bodies.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return authenticated ? Json(Payload()) : new(HttpStatusCode.Unauthorized) { Content = new StringContent("不正な非公開latest") };
        });
        var draft = Draft(); await Assert.ThrowsAsync<AuthenticationRejectedException>(() => f.Workspace.CreateAsync(draft)); Assert.Null(f.Store.Current);
        authenticated = true; Assert.Equal(user, (await f.Session.AcceptLoginAsync(new("new-login", "new-refresh", DateTimeOffset.UtcNow.AddMinutes(10))))!.AuthUserId);
        await f.Workspace.CreateAsync(draft); Assert.Equal(bodies[0], bodies[1]); Assert.Equal(draft.RequestId, JsonSerializer.Deserialize<JsonElement>(bodies[1]).GetProperty("requestId").GetGuid());
    }

    [Fact]
    public async Task ExternalAccountChangeStopsUnsavedOperationAndKeepsNewLogin()
    {
        using var f = new Fixture(_ => Json(Payload())); await f.Workspace.ListAsync();
        f.Store.Current = new("another", "another-refresh", DateTimeOffset.UtcNow.AddMinutes(10));
        await Assert.ThrowsAsync<AuthenticationContextChangedException>(() => f.Workspace.CreateAsync(Draft())); Assert.Single(f.Paths); Assert.Equal("another", f.Store.Current.AccessToken);
    }

    [Fact]
    public async Task AccountChangeDuringRefreshDoesNotRetryOrOverwriteNewLogin()
    {
        PeopleWorkspaceTests.Store? store = null;
        using var f = new Fixture(request => {
            if (request.RequestUri!.AbsolutePath == "/connect/token")
            {
                store!.Current = new("another", "another-refresh", DateTimeOffset.UtcNow.AddMinutes(10));
                return Refreshed();
            }
            return new(HttpStatusCode.Unauthorized);
        });
        store = f.Store;
        await Assert.ThrowsAsync<AuthenticationContextChangedException>(() => f.Workspace.CreateAsync(Draft()));
        Assert.Equal(2, f.Paths.Count); Assert.Equal("another", f.Store.Current!.AccessToken);
    }

    [Fact]
    public async Task PreCancelledOperationDoesNotSendOrClearAuthentication()
    {
        using var f = new Fixture(_ => Json(Payload())); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Workspace.ListAsync(cancellation.Token)); Assert.Empty(f.Paths); Assert.NotNull(f.Store.Current);
    }

    private static Task Act(Fixture f, string operation, CategoryCreationDraft? draft = null) => operation switch {
        "get" => f.Workspace.ListAsync(), "create" => f.Workspace.CreateAsync(draft ?? Draft()), "patch" => f.Workspace.UpdateAsync(Row, new("未保存")),
        "delete" => f.Workspace.DeleteAsync(Row), _ => f.Workspace.SaveOrderAsync(State.OrderRevision, State.Categories.Select(r => r.CategoryId).ToArray())
    };
    private static JsonElement Body(HttpRequestMessage request) => JsonSerializer.Deserialize<JsonElement>(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
    private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(value) };
    private static HttpResponseMessage Refreshed() => Json(new { access_token = "updated", refresh_token = "rotated", token_type = "Bearer", expires_in = 900 });
    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient http;
        public PeopleWorkspaceTests.Store Store { get; } = new();
        public List<(HttpMethod, string)> Paths { get; } = [];
        public CategoriesWorkspace Workspace { get; }
        public MonetailApiClient Api { get; }
        public ClientSession Session { get; }
        public Fixture(Func<HttpRequestMessage, HttpResponseMessage> respond) : this((request, _) => Task.FromResult(respond(request))) { }
        public Fixture(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        {
            var endpoint = ApiEndpoint.Parse("https://synthetic-api.example");
            http = new(new Handler((request, token) => {
                Assert.Equal("https", request.RequestUri!.Scheme); if (request.RequestUri.AbsolutePath != "/connect/token") Assert.NotNull(request.Headers.Authorization);
                Paths.Add((request.Method, request.RequestUri.AbsolutePath)); return respond(request, token);
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
