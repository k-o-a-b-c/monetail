using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Persistence;
using Xunit;

namespace Monetail.Api.Tests;

public sealed partial class CategoryApiTests
{
    private static Task<JsonElement> Order(Fixture f, string[] ids, string? revision = null, int status = 200) =>
        f.Call("PUT", new { revision = revision ?? f.Order(), categoryIds = ids }, status, "/order");

    [Fact]
    public async Task OrderRenumbersMixedAndDisabledCategoriesWithoutChangingAttributesOrOtherVersions()
    {
        await using var f = await Fixture.Create();
        var a = await f.Category("給与", "Income", "Exempt"); var b = await f.Category("食費", tax: "ReducedFood"); var c = await f.Category("新聞", tax: "ReducedNewspaper");
        await f.Call("PATCH", new { revision = Revision(b), validTo = "2000-01-01" }, suffix: Path(b));
        f.App.Database.Execute($"UPDATE ledger_categories SET display_order=7 WHERE category_id='{Id(a)}'; UPDATE ledger_categories SET display_order=7 WHERE category_id='{Id(b)}'; UPDATE ledger_categories SET display_order=12 WHERE category_id='{Id(c)}'");
        var other = (await Send(f.Client, "POST", "/ledgers", new { requestId = Guid.NewGuid(), name = "別家計簿" }, 201)).GetProperty("ledgerId").GetString()!;
        await Send(f.Client, "POST", $"/ledgers/{other}/categories", CreateInput("別科目"), 201);
        var person = (await Send(f.Client, "POST", $"/ledgers/{f.Ledger}/people", new { requestId = Guid.NewGuid(), name = "人物" }, 201)).GetProperty("people")[0].GetProperty("personId").GetString();
        await Send(f.Client, "POST", $"/ledgers/{f.Ledger}/payment-methods", new { requestId = Guid.NewGuid(), ownerPersonId = person, name = "現金" }, 201);
        var before = await f.Call("GET"); var header = f.Header();
        var unrelated = f.App.Database.Snapshot(f.App.Database.Tables().Where(t => t is not "ledgers" and not "ledger_categories"));
        var otherBefore = await Send(f.Client, "GET", $"/ledgers/{other}/categories");
        var result = await Order(f, [Id(c), Id(b), Id(a)]);
        Assert.Equal(new[] { Id(c), Id(b), Id(a) }, result.GetProperty("categories").EnumerateArray().Select(Id));
        Assert.Equal(new[] { 0, 1, 2 }, result.GetProperty("categories").EnumerateArray().Select(r => r.GetProperty("displayOrder").GetInt32()));
        Assert.Equal("Admin", result.GetProperty("currentRole").GetString());
        Assert.NotEqual(before.GetProperty("orderRevision").GetString(), f.Order()); AssertUuid(f.Order());
        Assert.Equal(f.Order(), result.GetProperty("orderRevision").GetString()); Assert.Equal(header, f.Header());
        foreach (var row in result.GetProperty("categories").EnumerateArray())
        {
            var old = before.GetProperty("categories").EnumerateArray().Single(r => Id(r) == Id(row));
            foreach (var field in new[] { "categoryId", "name", "incomeExpense", "taxCategoryCode", "validFrom", "validTo", "revision" })
                Assert.Equal(old.GetProperty(field).GetRawText(), row.GetProperty(field).GetRawText());
        }
        Assert.Equal(unrelated, f.App.Database.Snapshot(f.App.Database.Tables().Where(t => t is not "ledgers" and not "ledger_categories")));
        Assert.Equal(otherBefore.GetRawText(), (await Send(f.Client, "GET", $"/ledgers/{other}/categories")).GetRawText());
        Assert.Equal(result.GetRawText(), (await f.Call("GET")).GetRawText());
    }

    [Theory]
    [InlineData("Admin", 200, false)] [InlineData("Editor", 403, false)] [InlineData("Viewer", 403, false)]
    [InlineData("Admin", 200, true)] [InlineData("Editor", 403, true)] [InlineData("Viewer", 403, true)]
    public async Task OrderOnlyLedgerAdminCanWriteEvenForGlobalAdmin(string role, int status, bool unchanged)
    {
        await using var f = await Fixture.Create(); var a = await f.Category("A"); var b = await f.Category("B"); f.Role(role); var before = f.Snapshot();
        var result = await Order(f, unchanged ? [Id(a), Id(b)] : [Id(b), Id(a)], status: status);
        if (status == 403) { Assert.Equal(before, f.Snapshot()); AssertNoLatest(result); }
    }

    [Fact]
    public async Task OrderLedgerAdminDoesNotNeedGlobalRole()
    {
        await using var f = await Fixture.Create(); f.App.Database.Execute("DELETE FROM AspNetUserRoles"); await Order(f, []);
    }

    [Theory]
    [InlineData(0, "normal")]
    [InlineData(1, "normal")]
    [InlineData(2, "normal")]
    [InlineData(3, "gaps")]
    [InlineData(3, "ties")]
    public async Task OrderSameSequencePreservesPositionsAndAllVersions(int count, string positions)
    {
        await using var f = await Fixture.Create();
        for (var i = 0; i < count; i++)
            await f.Category("科目" + i);
        if (positions == "gaps")
            f.App.Database.Execute("UPDATE ledger_categories SET display_order=display_order*7+5");
        if (positions == "ties")
            f.App.Database.Execute("UPDATE ledger_categories SET display_order=7");
        var expected = await f.Call("GET");
        var ids = expected.GetProperty("categories").EnumerateArray().Select(Id).ToArray();
        var revision = f.Order();
        // 無変更経路でSQL UPDATE自体を行わないことも確認する。
        f.App.Database.Execute("CREATE TRIGGER must_not_write_category BEFORE UPDATE ON ledger_categories BEGIN SELECT RAISE(ABORT,'unexpected_write'); END; " +
            "CREATE TRIGGER must_not_write_order BEFORE UPDATE OF category_order_revision ON ledgers BEGIN SELECT RAISE(ABORT,'unexpected_write'); END");
        var before = f.Snapshot();
        foreach (var format in new[] { "N", "D", "B", "P", "X" })
        {
            var result = await Order(f, ids.Select(id => Guid.Parse(id).ToString(format).ToUpperInvariant()).ToArray(), Guid.Parse(revision).ToString(format));
            Assert.Equal(expected.GetRawText(), result.GetRawText());
            Assert.Equal(before, f.Snapshot());
        }
        AssertCategoryOrderLatest(await Order(f, ids, Guid.NewGuid().ToString("N"), 409), expected);
        Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task OrderEmptyAndSingleStillValidateTheEntireIdSet(int count)
    {
        await using var f = await Fixture.Create();
        if (count == 1)
            await f.Category("科目");
        var before = f.Snapshot();
        var error = await Order(f, [Guid.NewGuid().ToString("N")], status: 400);
        Assert.Equal("invalid_input", Code(error));
        Assert.Equal("categoryIds", error.GetProperty("field").GetString());
        Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("duplicate")] [InlineData("normalizedDuplicate")] [InlineData("missing")] [InlineData("extra")]
    [InlineData("foreign")] [InlineData("unknown")] [InlineData("badUuid")] [InlineData("emptyUuid")]
    [InlineData("numberElement")] [InlineData("nullElement")] [InlineData("arrayElement")]
    [InlineData("null")] [InlineData("object")] [InlineData("string")] [InlineData("number")] [InlineData("absent")]
    public async Task OrderRejectsInvalidFullCollectionWithoutWriting(string kind)
    {
        await using var f = await Fixture.Create(); var a = Id(await f.Category("A")); var b = Id(await f.Category("B"));
        var other = (await Send(f.Client, "POST", "/ledgers", new { requestId = Guid.NewGuid(), name = "別家計簿" }, 201)).GetProperty("ledgerId").GetString();
        var foreign = (await Send(f.Client, "POST", $"/ledgers/{other}/categories", CreateInput(), 201)).GetProperty("categoryId").GetString();
        object? ids = kind switch
        {
            "duplicate" => new[] { a, a }, "normalizedDuplicate" => new[] { a, Guid.Parse(a).ToString("D").ToUpperInvariant() },
            "missing" => new[] { a }, "extra" => new[] { a, b, Guid.NewGuid().ToString() }, "foreign" => new[] { a, foreign },
            "unknown" => new[] { a, Guid.NewGuid().ToString() }, "badUuid" => new[] { a, "invalid" }, "emptyUuid" => new[] { a, Guid.Empty.ToString() },
            "numberElement" => new object[] { a, 1 }, "nullElement" => new object?[] { a, null }, "arrayElement" => new object[] { a, new[] { b } },
            "null" => null, "object" => new { a, b }, "string" => a, "number" => 1, _ => new[] { a, b }
        };
        var before = f.Snapshot(); object input = kind == "absent" ? new { revision = f.Order() } : new { revision = f.Order(), categoryIds = ids };
        Assert.Equal("invalid_input", Code(await f.Call("PUT", input, 400, "/order"))); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("missingRevision")] [InlineData("badRevision")] [InlineData("emptyRevision")] [InlineData("nullRevision")]
    [InlineData("numberRevision")] [InlineData("extra")] [InlineData("array")] [InlineData("duplicateProperty")] [InlineData("malformed")]
    public async Task OrderRejectsInvalidEnvelope(string kind)
    {
        await using var f = await Fixture.Create(); var before = f.Snapshot(); var revision = f.Order();
        var body = kind switch
        {
            "missingRevision" => "{\"categoryIds\":[]}", "badRevision" => "{\"revision\":\"invalid\",\"categoryIds\":[]}",
            "emptyRevision" => JsonSerializer.Serialize(new { revision = Guid.Empty, categoryIds = Array.Empty<string>() }),
            "nullRevision" => "{\"revision\":null,\"categoryIds\":[]}", "numberRevision" => "{\"revision\":1,\"categoryIds\":[]}",
            "extra" => JsonSerializer.Serialize(new { revision, categoryIds = Array.Empty<string>(), name = "禁止" }),
            "array" => "[]", "duplicateProperty" => "{\"revision\":\"" + revision + "\",\"categoryIds\":[],\"categoryIds\":[]}", _ => "{"
        };
        using var response = await f.Client.PutAsync(f.Root + "/order", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("unknown")] [InlineData("notMember")]
    public async Task OrderLedgerBoundaryDoesNotLeakData(string kind)
    {
        await using var f = await Fixture.Create(); var ledger = Guid.NewGuid().ToString("N");
        if (kind == "notMember") { ledger = f.Ledger; f.App.Database.Execute("DELETE FROM ledger_members"); }
        var before = f.Snapshot(); var error = await Send(f.Client, "PUT", $"/ledgers/{ledger}/categories/order", new { revision = f.Order(), categoryIds = Array.Empty<string>() }, 404);
        AssertNoLatest(error); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("add")] [InlineData("delete")]
    public async Task OrderStaleAfterCompositionChangeReturnsCompleteCurrentList(string change)
    {
        await using var f = await Fixture.Create(); var a = await f.Category("収入", "Income", "Exempt"); var b = await f.Category("支出"); var revision = f.Order();
        await f.Call("PATCH", new { revision = Revision(a), name = "最新の収入", validTo = "2000-01-01", taxCategoryCode = "ReducedNewspaper" }, suffix: Path(a));
        if (change == "add") await f.Category("追加", tax: "ReducedFood");
        else await f.Call("DELETE", new { revision = Revision(b) }, suffix: Path(b));
        var expected = await f.Call("GET"); var before = f.Snapshot();
        var conflict = await Order(f, [Id(b), Id(a)], revision, 409);
        AssertCategoryOrderLatest(conflict, expected); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task OrderConcurrentSaveCannotOverwriteWithOldRevision()
    {
        await using var f = await Fixture.Create(); var a = Id(await f.Category("A")); var b = Id(await f.Category("B")); var revision = f.Order();
        var inputs = new[] { new[] { a, b }, new[] { b, a } };
        var responses = await Task.WhenAll(inputs.Select(ids => f.Client.PutAsJsonAsync(f.Root + "/order", new { revision, categoryIds = ids })));
        try
        {
            // 無変更が先なら版を消費しないため両方200。変更が先なら無変更は409。
            Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);
            Assert.True(responses[0].StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict);
            var changed = await responses[1].Content.ReadFromJsonAsync<JsonElement>();
            Assert.NotEqual(revision, changed.GetProperty("orderRevision").GetString());
            if (responses[0].StatusCode == HttpStatusCode.Conflict)
                AssertCategoryOrderLatest(await responses[0].Content.ReadFromJsonAsync<JsonElement>(), changed);
            else
            {
                var unchanged = await responses[0].Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(revision, unchanged.GetProperty("orderRevision").GetString());
                Assert.Equal(inputs[0], unchanged.GetProperty("categories").EnumerateArray().Select(Id));
            }
            Assert.Equal(inputs[1], (await f.Call("GET")).GetProperty("categories").EnumerateArray().Select(Id));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Theory]
    [InlineData("add")] [InlineData("delete")]
    public async Task OrderConcurrentWithCrudSerializesCompositionAndOrder(string change)
    {
        await using var f = await Fixture.Create(); var a = await f.Category("A"); var b = await f.Category("B"); var revision = f.Order();
        var reorder = f.Client.PutAsJsonAsync(f.Root + "/order", new { revision, categoryIds = new[] { Id(b), Id(a) } });
        var crud = f.Client.SendAsync(new HttpRequestMessage(change == "add" ? HttpMethod.Post : HttpMethod.Delete, f.Root + (change == "add" ? "" : Path(a)))
        { Content = JsonContent.Create<object>(change == "add" ? CreateInput("C") : new { revision = Revision(a) }) });
        var responses = await Task.WhenAll(reorder, crud);
        try
        {
            Assert.Equal(change == "add" ? HttpStatusCode.Created : HttpStatusCode.OK, responses[1].StatusCode);
            Assert.True(responses[0].StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict);
            var result = await f.Call("GET"); var rows = result.GetProperty("categories").EnumerateArray().ToArray();
            Assert.Equal(change == "add" ? 3 : 1, rows.Length); Assert.Equal(rows.Length, rows.Select(Id).Distinct().Count());
            Assert.NotEqual(revision, f.Order());
            if (responses[0].StatusCode == HttpStatusCode.Conflict)
                AssertCategoryOrderLatest(await responses[0].Content.ReadFromJsonAsync<JsonElement>(), result);
            else Assert.Equal(change == "add" ? new[] { "B", "A", "C" } : new[] { "B" }, rows.Select(r => r.GetProperty("name").GetString()));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task OrderConditionalDatabaseCheckRejectsIgnoredCasWithoutAnyChanges()
    {
        await using var f = await Fixture.Create(); var a = await f.Category("A"); var b = await f.Category("B");
        f.App.Database.Execute("CREATE TRIGGER ignore_order BEFORE UPDATE OF category_order_revision ON ledgers BEGIN SELECT RAISE(IGNORE); END");
        var expected = await f.Call("GET"); var before = f.Snapshot();
        AssertCategoryOrderLatest(await Order(f, [Id(b), Id(a)], status: 409), expected); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("version")] [InlineData("secondRow")]
    public async Task OrderDatabaseFailureRollsBackEveryRowAndOrderRevision(string stage)
    {
        await using var f = await Fixture.Create(); var a = await f.Category("A"); var b = await f.Category("B");
        f.App.Database.Execute(stage == "version" ? "CREATE TRIGGER fail_order BEFORE UPDATE OF category_order_revision ON ledgers BEGIN SELECT RAISE(ABORT,'synthetic'); END" :
            "CREATE TABLE test_category_order_writes(value INTEGER); CREATE TRIGGER fail_order BEFORE UPDATE OF display_order ON ledger_categories BEGIN INSERT INTO test_category_order_writes VALUES(1); SELECT CASE WHEN (SELECT COUNT(*) FROM test_category_order_writes)=2 THEN RAISE(ABORT,'synthetic') END; END");
        var before = f.Snapshot(); Assert.Equal("store_unavailable", Code(await Order(f, [Id(b), Id(a)], status: 503))); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("role", 403)]
    [InlineData("membership", 404)]
    [InlineData("session", 401)]
    [InlineData("expiry", 401)]
    public async Task OrderSameSequenceRechecksAuthorityAfterReadingSuccessResponse(string change, int status)
    {
        await using var f = await Fixture.Create();
        var a = await f.Category("非公開科目");
        var mutation = change switch
        {
            "role" => "UPDATE ledger_members SET role='Viewer'",
            "membership" => "DELETE FROM ledger_members",
            _ => "UPDATE auth_sessions SET RevokedAtUtc=CreatedAtUtc"
        };
        var interceptor = new OrderReadMutation(f.App.Time, mutation, change == "expiry", 2);
        using var host = f.App.NewFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddDbContext<ApiDbContext>(options => options.AddInterceptors(interceptor))));
        using var client = f.App.Client(host);
        await f.App.LoginAsync(client, count: 1);
        await LoginTestApp.CsrfAsync(client);
        var before = f.Snapshot();
        interceptor.Armed = true;
        var error = await Send(client, "PUT", f.Root + "/order", new { revision = f.Order(), categoryIds = new[] { Id(a) } }, status);
        Assert.False(interceptor.Armed);
        AssertNoLatest(error);
        Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("beforeCas", "role")] [InlineData("beforeCas", "membership")] [InlineData("beforeCas", "session")]
    [InlineData("afterCas", "role")] [InlineData("afterCas", "membership")] [InlineData("afterCas", "session")]
    [InlineData("afterRow", "role")] [InlineData("afterRow", "membership")] [InlineData("afterRow", "session")]
    [InlineData("conflict", "role")] [InlineData("conflict", "membership")] [InlineData("conflict", "session")] [InlineData("conflict", "expiry")]
    public async Task OrderRechecksCurrentAuthorityBeforeWritesAndCommitAndProtectsLatest(string stage, string change)
    {
        await using var f = await Fixture.Create(); var a = await f.Category("最新の非公開科目A"); var b = await f.Category("B");
        var mutation = change switch { "role" => "UPDATE ledger_members SET role='Viewer'", "membership" => "DELETE FROM ledger_members", _ => "UPDATE auth_sessions SET RevokedAtUtc=CreatedAtUtc" };
        if (stage is "afterCas" or "afterRow")
            f.App.Database.Execute($"CREATE TRIGGER change_actor AFTER UPDATE OF {(stage == "afterCas" ? "category_order_revision ON ledgers" : "display_order ON ledger_categories")} BEGIN {mutation}; END");
        // 直前の検証を省略して書き込んだ場合は503になるよう、書込防止トリガーも置く。
        if (stage == "beforeCas") f.App.Database.Execute("CREATE TRIGGER must_not_write BEFORE UPDATE OF category_order_revision ON ledgers BEGIN SELECT RAISE(ABORT,'write_before_authorization'); END");
        if (stage == "afterCas") f.App.Database.Execute("CREATE TRIGGER must_not_write BEFORE UPDATE ON ledger_categories BEGIN SELECT RAISE(ABORT,'write_before_authorization'); END");
        var interceptor = new OrderReadMutation(f.App.Time, mutation, change == "expiry");
        using var host = f.App.NewFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddDbContext<ApiDbContext>(options => options.AddInterceptors(interceptor))));
        using var client = f.App.Client(host); await f.App.LoginAsync(client, count: 1); await LoginTestApp.CsrfAsync(client);
        var before = f.Snapshot(); interceptor.Armed = stage is "beforeCas" or "conflict";
        var error = await Send(client, "PUT", f.Root + "/order", new { revision = stage == "conflict" ? Guid.NewGuid().ToString("N") : f.Order(), categoryIds = new[] { Id(b), Id(a) } }, change is "session" or "expiry" ? 401 : change == "membership" ? 404 : 403);
        AssertNoLatest(error); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("anonymous", 401)] [InlineData("csrf", 400)] [InlineData("origin", 403)] [InlineData("wrongOrigin", 403)]
    public async Task OrderUsesExistingAuthenticationCsrfAndOriginBoundary(string kind, int status)
    {
        await using var f = await Fixture.Create(); using var anonymous = f.App.Client(); var client = kind == "anonymous" ? anonymous : f.Client;
        if (kind == "csrf") client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        if (kind is "origin" or "wrongOrigin") { client.DefaultRequestHeaders.Remove("Origin"); if (kind == "wrongOrigin") client.DefaultRequestHeaders.Add("Origin", "https://other.example.test"); }
        var before = f.Snapshot(); await Send(client, "PUT", f.Root + "/order", new { revision = f.Order(), categoryIds = Array.Empty<string>() }, status); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task OrderSharesExistingWriteLimitAndDoesNotConsumeReadLimit()
    {
        await using var f = await Fixture.Create();
        for (var i = 0; i < 19; i++) await Order(f, []); // 家計簿作成を含め既存の20回枠を消費。
        using var denied = await f.Client.PutAsJsonAsync(f.Root + "/order", new { revision = f.Order(), categoryIds = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.TooManyRequests, denied.StatusCode); await f.Call("GET");
    }

    private static void AssertCategoryOrderLatest(JsonElement error, JsonElement expected)
    {
        Assert.Equal("revision_conflict", Code(error)); Assert.Equal(expected.GetRawText(), error.GetProperty("latest").GetRawText());
    }
    private static void AssertNoLatest(JsonElement error) => Assert.True(!error.TryGetProperty("latest", out var latest) || latest.ValueKind == JsonValueKind.Null);

    private sealed class OrderReadMutation(LoginTestApp.LoginTime time, string mutation, bool expire, int readNumber = 1) : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        private int reads;
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.Contains("FROM \"ledger_categories\"", StringComparison.Ordinal) && ++reads == readNumber)
            {
                Armed = false;
                if (expire) time.Advance(TimeSpan.FromDays(15));
                else
                {
                    using var update = command.Connection!.CreateCommand(); update.Transaction = command.Transaction;
                    update.CommandText = mutation; await update.ExecuteNonQueryAsync(cancellationToken);
                }
            }
            return result;
        }
    }
}
