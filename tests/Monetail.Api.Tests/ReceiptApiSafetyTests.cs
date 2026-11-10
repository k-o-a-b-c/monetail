using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Monetail.Api.Tests;

public sealed partial class ReceiptApiTests
{
    [Theory]
    [InlineData("tax", "POST")] [InlineData("category", "POST")] [InlineData("payment", "POST")] [InlineData("owner", "POST")] [InlineData("person", "POST")] [InlineData("expense", "POST")]
    [InlineData("tax", "same")] [InlineData("category", "same")] [InlineData("payment", "same")] [InlineData("owner", "same")] [InlineData("person", "same")] [InlineData("expense", "same")]
    [InlineData("tax", "date")] [InlineData("category", "date")] [InlineData("payment", "date")] [InlineData("owner", "date")] [InlineData("person", "date")] [InlineData("expense", "date")]
    [InlineData("tax", "value")] [InlineData("category", "value")] [InlineData("payment", "value")] [InlineData("owner", "value")] [InlineData("person", "value")] [InlineData("expense", "value")]
    public async Task MasterChangedRequiresSavedUnchangedDayAndRelevantValue(string kind, string mode)
    {
        await using var f = await Fixture.Create(); var id = await f.CreateReceipt(f.Input());
        var input = mode == "POST" ? f.Input() : f.Update(await f.Get(id));
        if (mode == "date") input["transactionAt"] = "2026-11-11T12:34:56";
        var payment = f.Payment; var person = kind == "person" ? f.Other : f.Owner; var category = f.Category;
        if (mode == "value")
        {
            switch (kind)
            {
                case "tax": Line(input)["taxRate"] = "8"; break;
                case "category": category = f.OtherCategory; Group(input)["categoryId"] = D(category); break;
                case "expense": Line(input)["expenseType"] = "Fixed"; break;
                case "person":
                    person = Guid.NewGuid().ToString("N");
                    f.App.Database.Execute($"INSERT INTO ledger_people(ledger_id,person_id,name,display_order,revision,payment_order_revision) VALUES('{f.Ledger}','{person}','子',2,'{Guid.NewGuid():N}','{Guid.NewGuid():N}')");
                    Line(input)["personIds"] = new JsonArray(D(person)); break;
                default: payment = f.OtherPayment; person = f.Other; input["paymentMethodId"] = D(payment); break;
            }
        }
        f.App.Database.Execute(kind switch
        {
            "tax" => "DELETE FROM tax_rate_history",
            "category" => $"UPDATE ledger_categories SET valid_to='2026-10-31' WHERE category_id='{category}'",
            "payment" => $"UPDATE ledger_payment_methods SET valid_to='2026-10-31' WHERE payment_method_id='{payment}'",
            "expense" => $"UPDATE ledger_categories SET income_expense='Income' WHERE category_id='{category}'",
            _ => $"UPDATE ledger_people SET valid_to='2026-10-31' WHERE person_id='{person}'"
        });
        var before = f.Snapshot(); var error = (await f.Call(mode == "POST" ? "POST" : "PUT", input, 400, mode == "POST" ? "" : "/" + id))!;
        Assert.Equal("invalid_input", error["code"]!.GetValue<string>()); Assert.NotNull(error["field"]);
        if (mode == "same") Assert.Equal("master_changed", error["reason"]!.GetValue<string>()); else Assert.False(error.ContainsKey("reason"));
        Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task MalformedChangedTaxStringIsNormalInputErrorAndNotAnUnhandledMasterError()
    {
        await using var f = await Fixture.Create(); var id = await f.CreateReceipt(f.Input()); var input = f.Update(await f.Get(id));
        Line(input)["taxRate"] = "not-a-number"; var before = f.Snapshot();
        var error = (await f.Call("PUT", input, 400, "/" + id))!; Assert.Equal("groups[0].details[0].taxRate", error["field"]!.GetValue<string>());
        Assert.False(error.ContainsKey("reason")); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("POST", "role")] [InlineData("PUT", "role")] [InlineData("DELETE", "role")]
    [InlineData("POST", "owner")] [InlineData("PUT", "owner")] [InlineData("DELETE", "owner")]
    [InlineData("POST", "session")] [InlineData("PUT", "session")] [InlineData("DELETE", "session")]
    [InlineData("POST", "member")] [InlineData("PUT", "member")] [InlineData("DELETE", "member")]
    public async Task AuthorizationChangesDuringSaveRollBackEntireTreeAndOperationRecord(string method, string change)
    {
        await using var f = await Fixture.Create(); string id = ""; var input = f.Input();
        if (method != "POST")
        {
            id = await f.CreateReceipt(input); var row = await f.Get(id);
            input = method == "PUT" ? f.Update(row) : new JsonObject { ["requestId"] = Guid.NewGuid().ToString("D"), ["revision"] = row["revision"]!.GetValue<string>() };
            if (method == "PUT") input["memo"] = "未保存";
        }
        f.Role("Editor", f.Owner);
        var mutation = change switch
        {
            "role" => "UPDATE ledger_members SET role='Viewer'", "owner" => $"UPDATE ledger_members SET person_id='{f.Other}'",
            "member" => "DELETE FROM ledger_members", _ => "UPDATE auth_sessions SET RevokedAtUtc=CreatedAtUtc"
        };
        var action = method == "POST" ? "INSERT" : method == "PUT" ? "UPDATE" : "DELETE";
        f.App.Database.Execute($"CREATE TRIGGER mutate_actor AFTER {action} ON ledger_receipts BEGIN {mutation}; END");
        var before = f.Snapshot(); var status = change == "session" ? 401 : change == "member" ? 404 : 403;
        var result = await f.Call(method, input, status, method == "POST" ? "" : "/" + id);
        if (result is not null) { Assert.False(result.ContainsKey("reason")); Assert.Null(result["latest"]); }
        Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("ledger_receipts")] [InlineData("ledger_receipt_groups")] [InlineData("ledger_receipt_details")]
    [InlineData("ledger_receipt_detail_people")] [InlineData("operation_requests")]
    public async Task CreateFailureAtEveryLayerLeavesNoPartialData(string table)
    {
        await using var f = await Fixture.Create();
        f.App.Database.Execute($"CREATE TRIGGER fail_write BEFORE INSERT ON {table} BEGIN SELECT RAISE(ABORT,'synthetic_failure'); END");
        var before = f.Snapshot(); var error = (await f.Call("POST", f.Input(), 503))!;
        Assert.Equal("store_unavailable", error["code"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("PUT", "detail")] [InlineData("PUT", "record")] [InlineData("DELETE", "detail")] [InlineData("DELETE", "record")]
    [InlineData("PUT", "ignore")] [InlineData("DELETE", "ignore")]
    public async Task UpdateDeleteFailuresAndCasIgnoreRestoreFullPreviousTree(string method, string kind)
    {
        await using var f = await Fixture.Create(); var id = await f.CreateReceipt(f.Input()); var row = await f.Get(id);
        var input = method == "PUT" ? f.Update(row) : new JsonObject { ["requestId"] = Guid.NewGuid().ToString("D"), ["revision"] = row["revision"]!.GetValue<string>() };
        if (method == "PUT") Line(input)["unitPrice"] = 200;
        var action = kind == "record" ? "INSERT" : method == "PUT" ? "UPDATE" : "DELETE";
        var table = kind == "record" ? "operation_requests" : kind == "ignore" ? "ledger_receipts" : "ledger_receipt_details";
        f.App.Database.Execute($"CREATE TRIGGER fail_write BEFORE {action} ON {table} BEGIN SELECT RAISE({(kind == "ignore" ? "IGNORE" : "ABORT,'synthetic_failure'")}); END");
        var before = f.Snapshot(); var error = (await f.Call(method, input, kind == "ignore" ? 409 : 503, "/" + id))!;
        if (kind == "ignore") Assert.True(JsonNode.DeepEquals(row, error["latest"]));
        Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task MasterChangeAfterBusinessWritesIsRevalidatedBeforeCommit()
    {
        await using var f = await Fixture.Create();
        f.App.Database.Execute("CREATE TRIGGER change_rates AFTER INSERT ON operation_requests WHEN NEW.operation_type='receipt.create' BEGIN DELETE FROM tax_rate_history; END");
        var before = f.Snapshot(); var error = (await f.Call("POST", f.Input(), 400))!;
        Assert.False(error.ContainsKey("reason")); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task MasterMutationApisProtectReferencedPeoplePaymentsAndCategoryIncome()
    {
        await using var f = await Fixture.Create(); await f.CreateReceipt(f.Input());
        async Task Check(string route, JsonObject request)
        {
            var before = f.Snapshot(); using var response = await f.Client.PatchAsJsonAsync(route, request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(before, f.Snapshot());
        }
        var personRevision = (string)f.App.Database.Scalar($"SELECT revision FROM ledger_people WHERE person_id='{f.Other}'")!;
        await Check($"/ledgers/{f.Ledger}/people/{f.Other}", new JsonObject { ["revision"] = personRevision, ["validTo"] = "2026-10-31" });
        personRevision = (string)f.App.Database.Scalar($"SELECT revision FROM ledger_people WHERE person_id='{f.Owner}'")!;
        await Check($"/ledgers/{f.Ledger}/people/{f.Owner}", new JsonObject { ["revision"] = personRevision, ["validFrom"] = "2026-11-11" });
        var payRevision = (string)f.App.Database.Scalar($"SELECT revision FROM ledger_payment_methods WHERE payment_method_id='{f.Payment}'")!;
        await Check($"/ledgers/{f.Ledger}/payment-methods/{f.Payment}", new JsonObject { ["revision"] = payRevision, ["validTo"] = "2026-10-31" });
        var catRevision = (string)f.App.Database.Scalar($"SELECT revision FROM ledger_categories WHERE category_id='{f.Category}'")!;
        await Check($"/ledgers/{f.Ledger}/categories/{f.Category}", new JsonObject { ["revision"] = catRevision, ["incomeExpense"] = "Income" });
    }

    [Fact]
    public async Task ActualForeignLedgerReferencesAndReceiptScopeReturn404WithoutLeakingRows()
    {
        await using var f = await Fixture.Create(); var id = await f.CreateReceipt(f.Input());
        async Task<JsonObject> Post(string path, object body)
        {
            using var response = await f.Client.PostAsJsonAsync(path, body); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        }
        var ledger = (await Post("/ledgers", new { requestId = Guid.NewGuid(), name = "別家計簿" }))["ledgerId"]!.GetValue<string>();
        var person = (await Post($"/ledgers/{ledger}/people", new { requestId = Guid.NewGuid(), name = "別人物" }))["people"]![0]!["personId"]!.GetValue<string>();
        var payment = (await Post($"/ledgers/{ledger}/payment-methods", new { requestId = Guid.NewGuid(), ownerPersonId = person, name = "別口座" }))["paymentMethodId"]!.GetValue<string>();
        var category = (await Post($"/ledgers/{ledger}/categories", new { requestId = Guid.NewGuid(), name = "別科目", incomeExpense = "Expense", taxCategoryCode = "Standard" }))["categoryId"]!.GetValue<string>();
        foreach (var kind in new[] { "payment", "category", "person" })
        {
            var input = f.Input();
            if (kind == "payment") input["paymentMethodId"] = D(payment);
            else if (kind == "category") Group(input)["categoryId"] = D(category);
            else Line(input)["personIds"] = new JsonArray(D(person));
            var before = f.Snapshot(); await f.Call("POST", input, 404); Assert.Equal(before, f.Snapshot());
        }
        using var get = await f.Client.GetAsync($"/ledgers/{D(ledger)}/receipts/{id}"); Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task DifferentActorCannotReuseSuccessfulRequestId()
    {
        await using var f = await Fixture.Create(); var input = f.Input(); await f.CreateReceipt(input);
        var account = await f.App.AddAccountAsync();
        f.App.Database.Execute($"INSERT INTO ledger_members(ledger_id,auth_user_id,role,joined_at_utc,revision,person_id) VALUES('{f.Ledger}','{account.UserId}','Admin','2026-11-10','{Guid.NewGuid():N}',NULL)");
        using var other = f.App.Client(); var options = await f.App.BeginAsync(other);
        using var login = await f.App.VerifyAsync(other, f.App.AssertionFor(options, account.UserId, account.CredentialId)); Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        await LoginTestApp.CsrfAsync(other); var before = f.Snapshot(); using var response = await other.PostAsJsonAsync(f.Root, input);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); Assert.Equal("request_conflict", (await response.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>());
        Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task DetailOrderingExemptZeroAndEmptyPersonArraysFollowTheContract()
    {
        await using var f = await Fixture.Create();
        f.App.Database.Execute($"INSERT INTO tax_rate_history(id,tax_category_code,valid_from,rate,revision) VALUES('{Guid.NewGuid():N}','ReducedFood','2026-01-01','0','{Guid.NewGuid():N}')");
        var input = f.Input(); var line = Line(input); line["taxRate"] = null; line["personIds"] = new JsonArray(); line["displayOrder"] = 2;
        var zero = line.DeepClone().AsObject(); zero["taxRate"] = "0"; zero["displayOrder"] = 0; Group(input)["details"]!.AsArray().Add(zero);
        var second = Group(input).DeepClone().AsObject(); second["categoryId"] = D(f.OtherCategory); input["groups"]!.AsArray().Insert(0, second);
        var row = await f.Get(await f.CreateReceipt(input)); Assert.Equal(D(f.Category), Group(row)["categoryId"]!.GetValue<string>());
        Assert.Equal("0", Line(row)["taxRate"]!.GetValue<string>()); Assert.Null(Line(row, index: 1)["taxRate"]); Assert.Empty(Line(row)["personIds"]!.AsArray());
    }

    [Fact]
    public async Task DuplicateJsonKeysAndInvalidUrlUuidsAreRejected()
    {
        await using var f = await Fixture.Create(); var text = f.Input().ToJsonString().Replace("\"shopName\":", "\"shopName\":\"duplicate\",\"shopName\":");
        var before = f.Snapshot(); using var content = new StringContent(text, Encoding.UTF8, "application/json");
        using var response = await f.Client.PostAsync(f.Root, content); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, f.Snapshot()); await f.Call("GET", null, 400, "/not-a-uuid");
    }
}
