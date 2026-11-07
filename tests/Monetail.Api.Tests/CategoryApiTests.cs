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

/// <summary>実HTTP・共通認証・隔離SQLiteで科目CRUDと保存の原子性を検証する。</summary>
public sealed partial class CategoryApiTests
{
    [Fact]
    public async Task CrudReplayNormalizationAndVersionsAreIndependent()
    {
        await using var f = await Fixture.Create();
        var original = await f.Call("GET"); Assert.Empty(original.GetProperty("categories").EnumerateArray());
        var header = f.Header(); var order = f.Order();
        var requestId = Guid.NewGuid();
        var created = await f.Call("POST", new { requestId, name = "　食費　", incomeExpense = "Expense", taxCategoryCode = "ReducedFood" }, 201);
        var row = Row(created); Assert.Equal("食費", row.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("validFrom").ValueKind); Assert.Equal(JsonValueKind.Null,row.GetProperty("validTo").ValueKind); Assert.Equal(0, row.GetProperty("displayOrder").GetInt32());
        AssertUuid(Id(row)); AssertUuid(Revision(row)); Assert.NotEqual(requestId.ToString("N"), Id(row));
        Assert.NotEqual(order, f.Order()); Assert.Equal(header, f.Header()); order = f.Order();
        var record = f.App.Database.Scalar("SELECT operation_type FROM operation_requests WHERE request_id='" + requestId.ToString("N") + "'");
        Assert.Equal("category.create", record);
        var replay = await f.Call("POST", new { requestId = requestId.ToString("N"), name = "食費", incomeExpense = "Expense", taxCategoryCode = "ReducedFood" });
        Assert.Equal(Id(row), replay.GetProperty("categoryId").GetString()); Assert.Equal(order, f.Order());
        var updated = Row(await f.Call("PATCH", new { revision = Revision(row), name = "生活費", taxCategoryCode = "Exempt", validTo = "2000-01-01" }, suffix: Path(row)));
        Assert.Equal("Expense", updated.GetProperty("incomeExpense").GetString()); Assert.Equal(Id(row), Id(updated));
        Assert.Equal("Exempt", updated.GetProperty("taxCategoryCode").GetString()); Assert.Equal("2000-01-01",updated.GetProperty("validTo").GetString());
        Assert.NotEqual(Revision(row), Revision(updated)); Assert.Equal(order, f.Order()); Assert.Equal(header, f.Header());
        var before = f.Snapshot();
        var unchanged = await f.Call("PATCH", new { revision = Revision(updated), name = " 生活費 ", taxCategoryCode = "Exempt", validTo = "2000-01-01" }, suffix: Path(row));
        Assert.Equal(Revision(updated), Revision(Row(unchanged))); Assert.Equal(before, f.Snapshot());
        var restored = Row(await f.Call("PATCH", new { revision = Revision(updated), validTo = (string?)null }, suffix: Path(row)));
        Assert.Equal(0, restored.GetProperty("displayOrder").GetInt32()); Assert.Equal(order, f.Order());
        var currentReplay = await f.Call("POST", new { requestId, name = "食費", incomeExpense = "Expense", taxCategoryCode = "ReducedFood" });
        Assert.Equal("生活費", Row(currentReplay).GetProperty("name").GetString());
        var removed = await f.Call("DELETE", new { revision = Revision(restored) }, suffix: Path(row));
        Assert.Empty(removed.GetProperty("categories").EnumerateArray()); Assert.NotEqual(order, f.Order()); Assert.Equal(header, f.Header());
        order = f.Order(); before = f.Snapshot();
        var deleted = await f.Call("POST", new { requestId, name = "食費", incomeExpense = "Expense", taxCategoryCode = "ReducedFood" }, 409);
        Assert.Equal("created_result_deleted", Code(deleted)); Assert.Equal(before, f.Snapshot()); Assert.Equal(order, f.Order());
    }

    [Theory]
    [InlineData("Income", "Standard")]
    [InlineData("Expense", "ReducedFood")]
    [InlineData("Income", "ReducedNewspaper")]
    [InlineData("Expense", "Exempt")]
    public async Task AllTaxCategoriesAndIncomeExpenseAreAccepted(string income, string tax)
    {
        await using var f = await Fixture.Create();
        var row = await f.Category(income: income, tax: tax);
        Assert.Equal(income, row.GetProperty("incomeExpense").GetString()); Assert.Equal(tax, row.GetProperty("taxCategoryCode").GetString());
    }

    [Theory]
    [InlineData("Admin", "POST", 201)] [InlineData("Editor", "POST", 403)] [InlineData("Viewer", "POST", 403)]
    [InlineData("Admin", "PATCH", 200)] [InlineData("Editor", "PATCH", 403)] [InlineData("Viewer", "PATCH", 403)]
    [InlineData("Admin", "DELETE", 200)] [InlineData("Editor", "DELETE", 403)] [InlineData("Viewer", "DELETE", 403)]
    public async Task OnlyLedgerAdminCanWriteEvenWhenActorIsGlobalAdmin(string role, string method, int status)
    {
        await using var f = await Fixture.Create(); var row = await f.Category(); f.Role(role); var before = f.Snapshot();
        object input = method == "POST" ? CreateInput("追加") : method == "PATCH" ? new { revision = Revision(row), name = "変更" } : new { revision = Revision(row) };
        await f.Call(method, input, status, method == "POST" ? "" : Path(row));
        if (status == 403) Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task LedgerAdminNeedsNoGlobalAdminRole()
    {
        await using var f = await Fixture.Create(); f.App.Database.Execute("DELETE FROM AspNetUserRoles");
        await f.Category();
    }

    [Theory]
    [InlineData("Admin")] [InlineData("Editor")] [InlineData("Viewer")]
    public async Task ReadsIncludeDisabledRowsInCommonOrderWithIdTieBreak(string role)
    {
        await using var f = await Fixture.Create(); var a = await f.Category("A"); var b = await f.Category("B"); var c = await f.Category("C");
        await f.Call("PATCH", new { revision = Revision(a), validTo = "2000-01-01" }, suffix: Path(a));
        f.App.Database.Execute($"UPDATE ledger_categories SET category_id='00000000000040008000000000000003',display_order=0 WHERE category_id='{Id(a)}';" +
            $"UPDATE ledger_categories SET category_id='00000000000040008000000000000001',display_order=0 WHERE category_id='{Id(b)}';" +
            $"UPDATE ledger_categories SET category_id='00000000000040008000000000000002',display_order=7 WHERE category_id='{Id(c)}'");
        f.Role(role); var before = f.Snapshot(); var result = await f.Call("GET");
        Assert.Equal(role, result.GetProperty("currentRole").GetString()); Assert.Equal(f.Order(), result.GetProperty("orderRevision").GetString());
        Assert.Equal(new[] { "B", "A", "C" }, result.GetProperty("categories").EnumerateArray().Select(row => row.GetProperty("name").GetString()));
        Assert.Equal("2000-01-01",result.GetProperty("categories")[1].GetProperty("validTo").GetString()); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task NamesAreTrimmedOrdinalAndUniqueIncludingDisabledCategories()
    {
        await using var f = await Fixture.Create(); var row = await f.Category("A");
        await f.Call("PATCH", new { revision = Revision(row), validTo = "2000-01-01" }, suffix: Path(row));
        await f.Category("a"); await f.Category(new string('あ', 100)); await f.Category(string.Concat(Enumerable.Repeat("😀", 50)));
        var before = f.Snapshot(); var duplicate = await f.Call("POST", CreateInput("　A "), 409);
        Assert.Equal("duplicate_category_name", Code(duplicate)); Assert.Equal("name", duplicate.GetProperty("field").GetString()); Assert.Equal(before, f.Snapshot());
        var other = await f.Category("別名"); before = f.Snapshot();
        Assert.Equal("duplicate_category_name", Code(await f.Call("PATCH", new { revision = Revision(other), name = " A " }, 409, Path(other))));
        Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("missingRequest")] [InlineData("missingName")] [InlineData("missingIncome")] [InlineData("missingTax")]
    [InlineData("badRequest")] [InlineData("emptyRequest")] [InlineData("nullName")] [InlineData("numberName")]
    [InlineData("blank")] [InlineData("long")] [InlineData("emoji")] [InlineData("badIncome")]
    [InlineData("nullIncome")] [InlineData("numberIncome")] [InlineData("caseIncome")]
    [InlineData("badTax")] [InlineData("nullTax")] [InlineData("numberTax")] [InlineData("caseTax")]
    [InlineData("isEnabled")] [InlineData("displayOrder")] [InlineData("cost")]
    public async Task InvalidCreateDoesNotChangeDataOrReplayRecord(string kind)
    {
        await using var f = await Fixture.Create(); var before = f.Snapshot();
        var input = new Dictionary<string, object?> { ["requestId"] = Guid.NewGuid(), ["name"] = "食費", ["incomeExpense"] = "Expense", ["taxCategoryCode"] = "Standard" };
        switch (kind)
        {
            case "missingRequest": input.Remove("requestId"); break;
            case "missingName": input.Remove("name"); break;
            case "missingIncome": input.Remove("incomeExpense"); break;
            case "missingTax": input.Remove("taxCategoryCode"); break;
            case "badRequest": input["requestId"] = "invalid"; break;
            case "emptyRequest": input["requestId"] = Guid.Empty; break;
            case "nullName": input["name"] = null; break;
            case "numberName": input["name"] = 1; break;
            case "blank": input["name"] = "　\t "; break;
            case "long": input["name"] = new string('a', 101); break;
            case "emoji": input["name"] = string.Concat(Enumerable.Repeat("😀", 51)); break;
            case "badIncome": input["incomeExpense"] = "Unknown"; break;
            case "nullIncome": input["incomeExpense"] = null; break;
            case "numberIncome": input["incomeExpense"] = 0; break;
            case "caseIncome": input["incomeExpense"] = "expense"; break;
            case "badTax": input["taxCategoryCode"] = "Unknown"; break;
            case "nullTax": input["taxCategoryCode"] = null; break;
            case "numberTax": input["taxCategoryCode"] = 1; break;
            case "caseTax": input["taxCategoryCode"] = "standard"; break;
            default: input[kind] = kind == "isEnabled" ? false : 1; break;
        }
        Assert.Equal("invalid_input", Code(await f.Call("POST", input, 400))); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("none")] [InlineData("missingRevision")] [InlineData("badRevision")] [InlineData("emptyRevision")]
    [InlineData("nameNull")] [InlineData("nameBlank")] [InlineData("nameLong")] [InlineData("taxNull")]
    [InlineData("taxUnknown")] [InlineData("enabledNull")] [InlineData("enabledString")] [InlineData("enabledNumber")]
    [InlineData("incomeExpense")] [InlineData("displayOrder")] [InlineData("cost")] [InlineData("requestId")]
    public async Task PatchRejectsUnsupportedFieldsNullsAndInvalidRevisionWithoutWrites(string kind)
    {
        await using var f = await Fixture.Create(); var row = await f.Category(); var before = f.Snapshot();
        var input = new Dictionary<string, object?> { ["revision"] = Revision(row), ["name"] = "変更" };
        switch (kind)
        {
            case "none": input.Remove("name"); break;
            case "missingRevision": input.Remove("revision"); break;
            case "badRevision": input["revision"] = "invalid"; break;
            case "emptyRevision": input["revision"] = Guid.Empty; break;
            case "nameNull": input["name"] = null; break;
            case "nameBlank": input["name"] = "　 "; break;
            case "nameLong": input["name"] = string.Concat(Enumerable.Repeat("😀", 51)); break;
            case "taxNull": input["taxCategoryCode"] = null; break;
            case "taxUnknown": input["taxCategoryCode"] = "Unknown"; break;
            case "enabledNull": input["isEnabled"] = null; break;
            case "enabledString": input["isEnabled"] = "true"; break;
            case "enabledNumber": input["isEnabled"] = 1; break;
            default: input[kind] = kind == "incomeExpense" ? "Unknown" : 1; break;
        }
        Assert.Equal("invalid_input", Code(await f.Call("PATCH", input, 400, Path(row)))); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task StrictJsonRejectsDuplicatePropertiesAndNonObjects()
    {
        await using var f = await Fixture.Create(); var before = f.Snapshot();
        foreach (var json in new[] { "[]", "null", "{", "{\"name\":\"A\",\"name\":\"B\"}" })
        {
            using var response = await f.Client.PostAsync(f.Root, new StringContent(json, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("missing")] [InlineData("null")] [InlineData("invalid")] [InlineData("empty")] [InlineData("unsupported")]
    public async Task DeleteRequiresOnlyAValidRevisionAndNeverChangesOrderOnInvalidInput(string kind)
    {
        await using var f = await Fixture.Create(); var row = await f.Category(); var before = f.Snapshot();
        var input = new Dictionary<string, object?>();
        if (kind != "missing") input["revision"] = kind switch
        {
            "null" => null, "invalid" => "invalid", "empty" => Guid.Empty, _ => Revision(row)
        };
        if (kind == "unsupported") input["name"] = "不可";
        Assert.Equal("invalid_input", Code(await f.Call("DELETE", input, 400, Path(row))));
        Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task LedgerPartitionAndRequestScopeDoNotLeakOrChangeOtherLedger()
    {
        await using var f = await Fixture.Create();
        var other = (await Send(f.Client, "POST", "/ledgers", new { requestId = Guid.NewGuid(), name = "別家計簿" }, 201)).GetProperty("ledgerId").GetString()!;
        var foreign = Row(await Send(f.Client, "POST", $"/ledgers/{other}/categories", CreateInput("同名"), 201));
        var ownRequest = Guid.NewGuid(); var own = Row(await f.Call("POST", CreateInput("同名", ownRequest), 201)); var before = f.Snapshot();
        await f.Call("PATCH", new { revision = Revision(foreign), name = "不可" }, 404, Path(foreign));
        await f.Call("DELETE", new { revision = Revision(foreign) }, 404, Path(foreign));
        var conflict = await Send(f.Client, "POST", $"/ledgers/{other}/categories", CreateInput("同名", ownRequest), 409);
        Assert.Equal("request_conflict", Code(conflict)); Assert.Equal(before, f.Snapshot());
        Assert.Equal(Id(own), Id(Row(await f.Call("GET"))));
        f.App.Database.Execute($"DELETE FROM ledger_members WHERE ledger_id='{other}'"); before = f.Snapshot();
        await Send(f.Client, "GET", $"/ledgers/{other}/categories", status: 404);
        await Send(f.Client, "POST", $"/ledgers/{other}/categories", CreateInput(), 404);
        Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task ReplayUsesCommonOperationActorAndNormalizedPayloadAndDeletedId()
    {
        await using var f = await Fixture.Create(); var request = Guid.NewGuid(); var row = Row(await f.Call("POST", CreateInput("A", request), 201));
        foreach (var input in new[] { CreateInput("B", request), CreateInput("A", request, "Income"), CreateInput("A", request, tax: "Exempt") })
            Assert.Equal("request_conflict", Code(await f.Call("POST", input, 409)));
        Assert.Equal("request_conflict", Code(await Send(f.Client, "POST", $"/ledgers/{f.Ledger}/people", new { requestId = request, name = "A" }, 409)));
        var account = await f.App.AddAccountAsync();
        f.App.Database.Execute($"INSERT INTO ledger_members(ledger_id,auth_user_id,role,joined_at_utc,revision) VALUES('{f.Ledger}','{account.UserId}','Admin','2026-01-01','{Guid.NewGuid():N}')");
        using var other = f.App.Client(); var options = await f.App.BeginAsync(other);
        using var login = await f.App.VerifyAsync(other, f.App.AssertionFor(options, account.UserId, account.CredentialId));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode); await LoginTestApp.CsrfAsync(other);
        Assert.Equal("request_conflict", Code(await Send(other, "POST", f.Root, CreateInput("A", request), 409)));
        await f.Call("DELETE", new { revision = Revision(row) }, suffix: Path(row)); await f.Category("A"); var before = f.Snapshot();
        Assert.Equal("created_result_deleted", Code(await f.Call("POST", CreateInput("A", request), 409))); Assert.Equal(before, f.Snapshot());
        f.Role("Viewer"); before = f.Snapshot(); await f.Call("POST", CreateInput("A", request), 403); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("PATCH")] [InlineData("DELETE")]
    public async Task StaleRowVersionReturnsLatestAndWritesNothing(string method)
    {
        await using var f = await Fixture.Create(); var row = await f.Category();
        var current = Row(await f.Call("PATCH", new { revision = Revision(row), name = "最新", taxCategoryCode = "Exempt", validTo = "2000-01-01" }, suffix: Path(row)));
        var before = f.Snapshot();
        var result = await f.Call(method, method == "PATCH" ? new { revision = Revision(row), name = "古い入力" } : new { revision = Revision(row) }, 409, Path(row));
        Assert.Equal("revision_conflict", Code(result)); Assert.Equal(current.GetRawText(), result.GetProperty("latest").GetRawText()); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("PATCH", "PATCH")] [InlineData("DELETE", "DELETE")] [InlineData("PATCH", "DELETE")]
    public async Task ConcurrentWritesCannotOverwriteWithTheSameOldRevision(string firstMethod, string secondMethod)
    {
        await using var f = await Fixture.Create(); var row = await f.Category();
        async Task<HttpResponseMessage> Request(string method) => await f.Client.SendAsync(new HttpRequestMessage(new HttpMethod(method), f.Root + Path(row))
        { Content = JsonContent.Create<object>(method == "PATCH" ? new { revision = Revision(row), name = Guid.NewGuid().ToString("N") } : new { revision = Revision(row) }) });
        var results = await Task.WhenAll(Request(firstMethod), Request(secondMethod));
        Assert.Single(results, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, response => response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound);
        foreach (var response in results) response.Dispose();
    }

    [Theory]
    [InlineData("distinct")] [InlineData("sameRequest")] [InlineData("sameName")]
    public async Task ConcurrentCreatesSerializeOrderUniquenessAndReplay(string kind)
    {
        await using var f = await Fixture.Create(); var request = Guid.NewGuid();
        var a = CreateInput("A", request); var b = CreateInput(kind == "distinct" ? "B" : "A", kind == "sameRequest" ? request : Guid.NewGuid());
        var responses = await Task.WhenAll(f.Client.PostAsJsonAsync(f.Root, a), f.Client.PostAsJsonAsync(f.Root, b));
        Assert.Equal(kind == "distinct" ? 2 : 1, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
        if (kind != "distinct") Assert.Single(responses, response => response.StatusCode == (kind == "sameRequest" ? HttpStatusCode.OK : HttpStatusCode.Conflict));
        foreach (var response in responses) response.Dispose();
        var result = await f.Call("GET"); var rows = result.GetProperty("categories").EnumerateArray().ToArray();
        Assert.Equal(kind == "distinct" ? 2 : 1, rows.Length); Assert.Equal(Enumerable.Range(0, rows.Length), rows.Select(row => row.GetProperty("displayOrder").GetInt32()));
        Assert.Equal((long)rows.Length, f.App.Database.Scalar("SELECT COUNT(*) FROM operation_requests WHERE operation_type='category.create'"));
    }

    [Fact]
    public async Task AppendIncludesDisabledGapsAndDoesNotChangeUnrelatedVersionsOrTaxHistory()
    {
        await using var f = await Fixture.Create(); var other = (await Send(f.Client, "POST", "/ledgers", new { requestId = Guid.NewGuid(), name = "別" }, 201)).GetProperty("ledgerId").GetString()!;
        var person = (await Send(f.Client, "POST", $"/ledgers/{f.Ledger}/people", new { requestId = Guid.NewGuid(), name = "本人" }, 201)).GetProperty("people")[0];
        await Send(f.Client, "POST", $"/ledgers/{f.Ledger}/payment-methods", new { requestId = Guid.NewGuid(), ownerPersonId = person.GetProperty("personId").GetString(), name = "現金" }, 201);
        var fixedData = f.App.Database.Snapshot(new[] { "ledger_people", "ledger_payment_methods", "tax_categories", "tax_rate_history" });
        var otherOrder = f.App.Database.Scalar($"SELECT category_order_revision FROM ledgers WHERE id='{other}'"); var header = f.Header();
        var first = await f.Category("先"); var disabled = await f.Category("無効");
        await f.Call("PATCH", new { revision = Revision(disabled), validTo = "2000-01-01" }, suffix: Path(disabled));
        f.App.Database.Execute($"UPDATE ledger_categories SET display_order=7 WHERE category_id='{Id(disabled)}'");
        await f.Call("DELETE", new { revision = Revision(first) }, suffix: Path(first));
        var next = await f.Category("後"); Assert.Equal(8, next.GetProperty("displayOrder").GetInt32());
        Assert.Equal(fixedData, f.App.Database.Snapshot(new[] { "ledger_people", "ledger_payment_methods", "tax_categories", "tax_rate_history" }));
        Assert.Equal(otherOrder, f.App.Database.Scalar($"SELECT category_order_revision FROM ledgers WHERE id='{other}'")); Assert.Equal(header, f.Header());
    }

    [Fact]
    public async Task RestrictReferenceBlocksDeleteWhileRenameChangesJoinedDisplayAndLeavesSavedTaxUntouched()
    {
        await using var f = await Fixture.Create(); var row = await f.Category();
        // 将来の参照FKと保存済み税値だけをテスト表で再現。レシート機能は仮実装しない。
        f.App.Database.Execute($"CREATE TABLE category_reference_probe(ledger_id TEXT NOT NULL,category_id TEXT NOT NULL,applied_tax TEXT,rate TEXT,FOREIGN KEY(ledger_id,category_id) REFERENCES ledger_categories(ledger_id,category_id) ON DELETE RESTRICT); INSERT INTO category_reference_probe VALUES('{f.Ledger}','{Id(row)}','Standard','10')");
        var saved = f.App.Database.Snapshot(new[] { "category_reference_probe" });
        row = Row(await f.Call("PATCH", new { revision = Revision(row), name = "新名称", taxCategoryCode = "Exempt" }, suffix: Path(row)));
        Assert.Equal(saved, f.App.Database.Snapshot(new[] { "category_reference_probe" }));
        Assert.Equal("新名称", f.App.Database.Scalar("SELECT c.name FROM category_reference_probe p JOIN ledger_categories c USING(ledger_id,category_id)"));
        var before = f.Snapshot(); var error = await f.Call("DELETE", new { revision = Revision(row) }, 409, Path(row));
        Assert.Equal("category_referenced", Code(error)); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("POST", "role")] [InlineData("POST", "exclude")] [InlineData("POST", "session")]
    [InlineData("PATCH", "role")] [InlineData("PATCH", "exclude")] [InlineData("PATCH", "session")]
    [InlineData("DELETE", "role")] [InlineData("DELETE", "exclude")] [InlineData("DELETE", "session")]
    public async Task FinalPermissionAndSessionRecheckRollsBackTheEntireWrite(string method, string mutation)
    {
        await using var f = await Fixture.Create(); var row = await f.Category();
        var sql = mutation switch { "role" => "UPDATE ledger_members SET role='Viewer'", "exclude" => "DELETE FROM ledger_members", _ => "UPDATE auth_sessions SET RevokedAtUtc=CreatedAtUtc" };
        var operation = method == "POST" ? "INSERT" : method == "PATCH" ? "UPDATE" : "DELETE";
        f.App.Database.Execute($"CREATE TRIGGER change_actor AFTER {operation} ON ledger_categories BEGIN {sql}; END"); var before = f.Snapshot();
        var status = mutation == "session" ? 401 : mutation == "exclude" ? 404 : 403;
        await f.Call(method, method == "POST" ? CreateInput("新規") : method == "PATCH" ? new { revision = Revision(row), name = "変更" } : new { revision = Revision(row) }, status, method == "POST" ? "" : Path(row));
        Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("POST")] [InlineData("DELETE")]
    public async Task PermissionIsRecheckedBeforeCategoryWriteAfterOrderCas(string method)
    {
        await using var f = await Fixture.Create(); var row = await f.Category();
        f.App.Database.Execute("CREATE TRIGGER lose_admin AFTER UPDATE OF category_order_revision ON ledgers BEGIN UPDATE ledger_members SET role='Viewer'; END;" +
            $"CREATE TRIGGER must_not_write BEFORE {(method == "POST" ? "INSERT" : "DELETE")} ON ledger_categories BEGIN SELECT RAISE(ABORT,'write_before_authorization'); END");
        var before = f.Snapshot(); await f.Call(method, method == "POST" ? CreateInput("新規") : new { revision = Revision(row) }, 403, method == "POST" ? "" : Path(row));
        Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("insert")] [InlineData("record")] [InlineData("orderCas")] [InlineData("orderFailure")]
    public async Task CreateFailureRollsBackCategoryOrderAndOperationRecord(string kind)
    {
        await using var f = await Fixture.Create();
        f.App.Database.Execute(kind switch
        {
            "insert" => "CREATE TRIGGER fail BEFORE INSERT ON ledger_categories BEGIN SELECT RAISE(ABORT,'synthetic'); END",
            "record" => "CREATE TRIGGER fail BEFORE INSERT ON operation_requests BEGIN SELECT RAISE(ABORT,'synthetic'); END",
            "orderCas" => "CREATE TRIGGER fail BEFORE UPDATE OF category_order_revision ON ledgers BEGIN SELECT RAISE(IGNORE); END",
            _ => "CREATE TRIGGER fail BEFORE UPDATE OF category_order_revision ON ledgers BEGIN SELECT RAISE(ABORT,'synthetic'); END"
        });
        var before = f.Snapshot(); var result = await f.Call("POST", CreateInput(), kind == "orderCas" ? 409 : 503);
        Assert.Equal(kind == "orderCas" ? "revision_conflict" : "store_unavailable", Code(result)); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("PATCH", "ignore")] [InlineData("PATCH", "fail")] [InlineData("DELETE", "ignore")] [InlineData("DELETE", "fail")]
    [InlineData("DELETE", "orderCas")]
    public async Task ConditionalRowAndOrderFailuresNeverPartiallySaveOrMisclassifyErrors(string method, string kind)
    {
        await using var f = await Fixture.Create(); var row = await f.Category();
        f.App.Database.Execute(kind == "orderCas" ? "CREATE TRIGGER fail BEFORE UPDATE OF category_order_revision ON ledgers BEGIN SELECT RAISE(IGNORE); END" :
            $"CREATE TRIGGER fail BEFORE {(method == "PATCH" ? "UPDATE" : "DELETE")} ON ledger_categories BEGIN SELECT RAISE({(kind == "ignore" ? "IGNORE" : "ABORT,'synthetic'")}); END");
        var before = f.Snapshot(); var error = await f.Call(method, method == "PATCH" ? new { revision = Revision(row), name = "変更" } : new { revision = Revision(row) }, kind == "fail" ? 503 : 409, Path(row));
        Assert.Equal(kind == "fail" ? "store_unavailable" : "revision_conflict", Code(error)); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("POST")] [InlineData("PATCH")]
    public async Task ActualDatabaseNameCollisionReturns409AndRollsBack(string method)
    {
        await using var f = await Fixture.Create(); var row = await f.Category();
        // 事前重複検証の後にDB側で衝突を発生させ、UNIQUE例外の分類も検証する。
        f.App.Database.Execute($"CREATE TRIGGER collide BEFORE {(method == "POST" ? "INSERT" : "UPDATE")} ON ledger_categories WHEN NEW.name='衝突' BEGIN " +
            "INSERT INTO ledger_categories(ledger_id,category_id,name,valid_to,display_order,tax_category_code,income_expense,revision) " +
            "VALUES(NEW.ledger_id,lower(hex(randomblob(16))),NEW.name,NULL,99,'Standard','Expense',lower(hex(randomblob(16)))); END");
        var before = f.Snapshot(); var error = await f.Call(method, method == "POST" ? CreateInput("衝突") : new { revision = Revision(row), name = "衝突" }, 409, method == "POST" ? "" : Path(row));
        Assert.Equal("duplicate_category_name", Code(error)); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("PATCH")] [InlineData("DELETE")]
    public async Task ExpiryDuringConflictReadNeverExposesLatest(string method)
    {
        await using var f = await Fixture.Create(); var row = await f.Category("公開しない最新名称");
        var interceptor = new ReadMutation(f.App.Time, "expire");
        using var host = f.App.NewFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddDbContext<ApiDbContext>(options => options.AddInterceptors(interceptor))));
        using var client = f.App.Client(host); await f.App.LoginAsync(client, count: 1); await LoginTestApp.CsrfAsync(client); interceptor.Armed = true;
        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), f.Root + Path(row))
        { Content = JsonContent.Create<object>(method == "PATCH" ? new { revision = Guid.NewGuid(), name = "未保存" } : new { revision = Guid.NewGuid() }) });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(); var error = JsonSerializer.Deserialize<JsonElement>(body);
        Assert.Equal("authentication_changed", Code(error));
        Assert.True(!error.TryGetProperty("latest", out var latest) || latest.ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task PatchRechecksAdminImmediatelyBeforeConditionalWrite()
    {
        await using var f = await Fixture.Create(); var row = await f.Category();
        f.App.Database.Execute("CREATE TRIGGER must_not_write BEFORE UPDATE ON ledger_categories BEGIN SELECT RAISE(ABORT,'write_before_authorization'); END");
        var interceptor = new ReadMutation(f.App.Time, "demote");
        using var host = f.App.NewFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddDbContext<ApiDbContext>(options => options.AddInterceptors(interceptor))));
        using var client = f.App.Client(host); await f.App.LoginAsync(client, count: 1); await LoginTestApp.CsrfAsync(client); var before = f.Snapshot(); interceptor.Armed = true;
        await Send(client, "PATCH", f.Root + Path(row), new { revision = Revision(row), name = "変更" }, 403);
        Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("anonymous", 401)] [InlineData("csrf", 400)] [InlineData("origin", 403)] [InlineData("wrongOrigin", 403)] [InlineData("notMember", 404)]
    public async Task AuthenticationCsrfOriginAndParticipationBoundariesAreShared(string kind, int status)
    {
        await using var f = await Fixture.Create(); using var anonymous = f.App.Client(); var client = kind == "anonymous" ? anonymous : f.Client;
        if (kind == "csrf") client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        if (kind is "origin" or "wrongOrigin") { client.DefaultRequestHeaders.Remove("Origin"); if (kind == "wrongOrigin") client.DefaultRequestHeaders.Add("Origin", "https://other.example.test"); }
        if (kind == "notMember") f.App.Database.Execute("DELETE FROM ledger_members"); var before = f.Snapshot();
        await Send(client, "POST", f.Root, CreateInput(), status); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task AppendOverflowDoesNotAdvanceOrderOrRecordRequest()
    {
        await using var f = await Fixture.Create(); await f.Category(); f.App.Database.Execute("UPDATE ledger_categories SET display_order=2147483647"); var before = f.Snapshot();
        Assert.Equal("invalid_input", Code(await f.Call("POST", CreateInput("追加不可"), 400))); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task WriteLimitIsSharedWithPeopleAndReadLimitIsIndependent()
    {
        await using var f = await Fixture.Create();
        for (var i = 0; i < 19; i++) await f.Call("POST", CreateInput(""), 400); // 家計簿作成で同じ20回枠を1回消費済み。
        using var denied = await f.Client.PostAsJsonAsync($"/ledgers/{f.Ledger}/people", new { requestId = Guid.NewGuid(), name = "不可" });
        Assert.Equal(HttpStatusCode.TooManyRequests, denied.StatusCode); await f.Call("GET");
    }

    [Fact]
    public async Task ReadLimitIsSharedWithPeopleAndDoesNotBlockCategoryWrites()
    {
        await using var f = await Fixture.Create();
        for (var i = 0; i < 120; i++) await f.Call("GET");
        using var denied = await f.Client.GetAsync($"/ledgers/{f.Ledger}/people"); Assert.Equal(HttpStatusCode.TooManyRequests, denied.StatusCode);
        await f.Category();
    }

    private sealed class ReadMutation(LoginTestApp.LoginTime time, string kind) : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Armed && kind == "demote" && command.CommandText.StartsWith("SELECT EXISTS", StringComparison.Ordinal) && command.CommandText.Contains("FROM \"ledger_categories\"", StringComparison.Ordinal))
            {
                Armed = false; using var mutation = command.Connection!.CreateCommand(); mutation.Transaction = command.Transaction;
                mutation.CommandText = "UPDATE ledger_members SET role='Viewer'"; await mutation.ExecuteNonQueryAsync(cancellationToken);
            }
            return result;
        }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Armed && kind == "expire" && command.CommandText.Contains("FROM \"ledger_categories\"", StringComparison.Ordinal))
            { Armed = false; time.Advance(TimeSpan.FromDays(15)); }
            return ValueTask.FromResult(result);
        }
    }

    private static object CreateInput(string name = "食費", Guid? requestId = null, string income = "Expense", string tax = "Standard") =>
        new { requestId = requestId ?? Guid.NewGuid(), name, incomeExpense = income, taxCategoryCode = tax };
    private static JsonElement Row(JsonElement response) => response.GetProperty("categories").EnumerateArray().Single();
    private static string Id(JsonElement row) => row.GetProperty("categoryId").GetString()!;
    private static string Revision(JsonElement row) => row.GetProperty("revision").GetString()!;
    private static string Path(JsonElement row) => "/" + Id(row);
    private static string? Code(JsonElement response) => response.GetProperty("code").GetString();
    private static void AssertUuid(string value) { Assert.True(Guid.TryParseExact(value, "N", out _)); Assert.Equal('4', value[12]); }
    private static async Task<JsonElement> Send(HttpClient client, string method, string path, object? input = null, int status = 200)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path); if (input is not null) request.Content = JsonContent.Create(input);
        using var response = await client.SendAsync(request); Assert.Equal((HttpStatusCode)status, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(); return string.IsNullOrEmpty(body) ? default : JsonSerializer.Deserialize<JsonElement>(body);
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public LoginTestApp App = null!;
        public HttpClient Client = null!;
        public string Ledger = "";
        public string Root => $"/ledgers/{Ledger}/categories";
        public static async Task<Fixture> Create()
        {
            var f = new Fixture { App = await LoginTestApp.CreateAsync() }; f.Client = f.App.Client(); await f.App.LoginAsync(f.Client); await LoginTestApp.CsrfAsync(f.Client);
            f.Ledger = (await Send(f.Client, "POST", "/ledgers", new { requestId = Guid.NewGuid(), name = "家計簿" }, 201)).GetProperty("ledgerId").GetString()!;
            return f;
        }
        public Task<JsonElement> Call(string method, object? input = null, int status = 200, string suffix = "") => Send(Client, method, Root + suffix, input, status);
        public async Task<JsonElement> Category(string name = "食費", string income = "Expense", string tax = "Standard")
        {
            var result = await Call("POST", CreateInput(name, income: income, tax: tax), 201);
            return result.GetProperty("categories").EnumerateArray().Single(row => Id(row) == result.GetProperty("categoryId").GetString());
        }
        public string Snapshot() => App.Database.Snapshot(App.Database.Tables());
        public string Order() => (string)App.Database.Scalar($"SELECT category_order_revision FROM ledgers WHERE id='{Ledger}'")!;
        public object? Header() => App.Database.Scalar($"SELECT revision || ':' || people_order_revision FROM ledgers WHERE id='{Ledger}'");
        public void Role(string role) => App.Database.Execute($"UPDATE ledger_members SET role='{role}' WHERE ledger_id='{Ledger}' AND auth_user_id='{App.UserId}'");
        public async ValueTask DisposeAsync() { Client.Dispose(); await App.DisposeAsync(); }
    }
}
