using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Monetail.Api.Tests;

public sealed partial class ReceiptApiTests
{
    [Fact]
    public async Task PostAndDetailHaveExactShapeDIdentifiersAndNoMutationOnRead()
    {
        await using var f = await Fixture.Create();
        var input = f.Input(); var id = await f.CreateReceipt(input); var before = f.Snapshot();
        var detail = await f.Get(id);
        Keys(detail, "receiptId", "transactionAt", "shopName", "shopLocation", "paymentMethodId", "taxInclusionMode", "taxRoundingMode", "recurrenceInterval", "recurrenceUnit", "memo", "revision", "groups");
        Assert.Equal("2026-11-10T12:34:56", detail["transactionAt"]!.GetValue<string>());
        foreach (var name in new[] { "shopLocation", "recurrenceInterval", "recurrenceUnit", "memo" }) Assert.Null(detail[name]);
        AssertD(detail["receiptId"]!, '7'); AssertD(detail["revision"]!, '4'); AssertD(detail["paymentMethodId"]!);
        var group = Group(detail); Keys(group, "groupId", "categoryId", "memo", "details"); AssertD(group["groupId"]!, '7');
        var line = Line(detail); Keys(line, "detailId", "displayOrder", "name", "unitPrice", "quantity", "unitDiscountAmount", "discountRate", "taxRate", "expenseType", "occurrenceType", "memo", "personIds");
        AssertD(line["detailId"]!, '7'); Assert.Null(group["memo"]); Assert.Null(line["memo"]);
        Assert.Equal(new[] { D(f.Owner), D(f.Other) }, line["personIds"]!.AsArray().Select(p => p!.GetValue<string>()));
        Assert.Equal(before, f.Snapshot());
        Assert.Equal(110L, f.App.Database.Scalar("SELECT total_amount FROM ledger_receipts"));
    }

    [Fact]
    public async Task PutMaintainsChildrenAndNoChangeLeavesRevisionAndAuditUntouched()
    {
        await using var f = await Fixture.Create(); var id = await f.CreateReceipt(f.Input()); var original = await f.Get(id);
        var request = f.Update(original); Line(request)["unitPrice"] = 200;
        await f.Call("PUT", request, 204, "/" + id); var changed = await f.Get(id);
        Assert.Equal(Group(original)["groupId"]!.GetValue<string>(), Group(changed)["groupId"]!.GetValue<string>());
        Assert.Equal(Line(original)["detailId"]!.GetValue<string>(), Line(changed)["detailId"]!.GetValue<string>());
        Assert.NotEqual(original["revision"]!.GetValue<string>(), changed["revision"]!.GetValue<string>());
        f.App.Database.Execute("CREATE TRIGGER no_parent_update BEFORE UPDATE ON ledger_receipts BEGIN SELECT RAISE(ABORT,'unchanged_parent_update'); END");
        var before = f.BusinessSnapshot(); var unchanged = f.Update(changed);
        Line(unchanged)["taxRate"] = "010.00"; Line(unchanged)["discountRate"] = "00.0";
        Line(unchanged)["personIds"] = new JsonArray(D(f.Other), D(f.Owner));
        await f.Call("PUT", unchanged, 204, "/" + id);
        Assert.Equal(before, f.BusinessSnapshot()); Assert.Equal(3L, f.App.Database.Scalar("SELECT COUNT(*) FROM operation_requests WHERE operation_type LIKE 'receipt.%'"));
    }

    [Fact]
    public async Task DeleteReplayUsesSuccessRecordWithoutRequiringDeletedResource()
    {
        await using var f = await Fixture.Create(); var id = await f.CreateReceipt(f.Input()); var detail = await f.Get(id);
        var input = new JsonObject { ["requestId"] = Guid.NewGuid().ToString("D"), ["revision"] = detail["revision"]!.GetValue<string>() };
        await f.Call("DELETE", input, 204, "/" + id); var before = f.Snapshot();
        await f.Call("DELETE", input, 204, "/" + id); Assert.Equal(before, f.Snapshot());
        foreach (var table in new[] { "ledger_receipts", "ledger_receipt_groups", "ledger_receipt_details", "ledger_receipt_detail_people" })
            Assert.Equal(0L, f.App.Database.Scalar("SELECT COUNT(*) FROM " + table));
        await f.Call("GET", null, 404, "/" + id);
    }

    private static JsonObject Group(JsonObject root, int index = 0) => root["groups"]![index]!.AsObject();
    private static JsonObject Line(JsonObject root, int group = 0, int index = 0) => Group(root, group)["details"]![index]!.AsObject();
    private static string D(string value) => Guid.Parse(value).ToString("D");
    private static void Keys(JsonObject value, params string[] keys) => Assert.Equal(keys.Order(), value.Select(p => p.Key).Order());
    private static void AssertD(JsonNode value, char? version = null)
    {
        var text = value.GetValue<string>(); Assert.True(Guid.TryParseExact(text, "D", out var id)); Assert.Equal(id.ToString("D"), text);
        if (version is char digit) Assert.Equal(digit, id.ToString("N")[12]);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public LoginTestApp App = null!; public HttpClient Client = null!;
        public string Ledger = "", Owner = "", Other = "", Payment = "", OtherPayment = "", Category = "", OtherCategory = "";
        public string Root => $"/ledgers/{D(Ledger)}/receipts";
        public static async Task<Fixture> Create()
        {
            var f = new Fixture { App = await LoginTestApp.CreateAsync() }; f.Client = f.App.Client();
            await f.App.LoginAsync(f.Client); await LoginTestApp.CsrfAsync(f.Client);
            async Task<JsonObject> Send(string path, object input)
            {
                using var response = await f.Client.PostAsJsonAsync(path, input); Assert.Equal(201, (int)response.StatusCode);
                return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
            }
            f.Ledger = (await Send("/ledgers", new { requestId = Guid.NewGuid(), name = "家計簿" }))["ledgerId"]!.GetValue<string>();
            async Task<string> Person(string name) => (await Send($"/ledgers/{f.Ledger}/people", new { requestId = Guid.NewGuid(), name }))["people"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == name)!["personId"]!.GetValue<string>();
            f.Owner = await Person("父"); f.Other = await Person("母");
            async Task<string> Payment(string owner) => (await Send($"/ledgers/{f.Ledger}/payment-methods", new { requestId = Guid.NewGuid(), ownerPersonId = owner, name = "現金" }))["paymentMethodId"]!.GetValue<string>();
            f.Payment = await Payment(f.Owner); f.OtherPayment = await Payment(f.Other);
            async Task<string> Category(string name) => (await Send($"/ledgers/{f.Ledger}/categories", new { requestId = Guid.NewGuid(), name, incomeExpense = "Expense", taxCategoryCode = "Standard" }))["categoryId"]!.GetValue<string>();
            f.Category = await Category("食費"); f.OtherCategory = await Category("日用品");
            f.App.Database.Execute($"INSERT INTO tax_rate_history(id,tax_category_code,valid_from,rate,revision) VALUES('{Guid.NewGuid():N}','Standard','2026-01-01','10','{Guid.NewGuid():N}')");
            return f;
        }
        public JsonObject Input() => JsonSerializer.SerializeToNode(new
        {
            requestId = Guid.NewGuid().ToString("D"), transactionAt = "2026-11-10T12:34:56", shopName = "店舗", shopLocation = (string?)null,
            paymentMethodId = D(Payment), taxInclusionMode = 1, taxRoundingMode = "floor", recurrenceInterval = (int?)null, recurrenceUnit = (string?)null, memo = (string?)null,
            groups = new[] { new { categoryId = D(Category), memo = (string?)null, details = new[] { new {
                displayOrder = 0, name = "明細", unitPrice = 100, quantity = 1, unitDiscountAmount = 0, discountRate = "0", taxRate = "10",
                expenseType = "Variable", occurrenceType = "Irregular", memo = (string?)null, personIds = new[] { D(Other), D(Owner) }
            } } } }
        })!.AsObject();
        public JsonObject Update(JsonObject detail)
        {
            var result = detail.DeepClone().AsObject(); result.Remove("receiptId"); result["requestId"] = Guid.NewGuid().ToString("D"); return result;
        }
        public async Task<string> CreateReceipt(JsonObject input)
        {
            using var response = await Client.PostAsJsonAsync(Root, input);
            var body = await response.Content.ReadAsStringAsync(); Assert.True(response.StatusCode == HttpStatusCode.Created, body);
            Assert.Empty(body); return response.Headers.Location!.OriginalString.Split('/').Last();
        }
        public async Task<JsonObject> Get(string id) => (await Call("GET", null, 200, "/" + id))!;
        public async Task<JsonObject?> Call(string method, JsonObject? input, int expected, string suffix = "")
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), Root + suffix);
            if (input is not null) request.Content = JsonContent.Create(input);
            using var response = await Client.SendAsync(request); var body = await response.Content.ReadAsStringAsync();
            Assert.True((int)response.StatusCode == expected, $"Expected {expected}, got {(int)response.StatusCode}: {body}");
            if (expected is 201 or 204) { Assert.Empty(body); return null; }
            return JsonNode.Parse(body)!.AsObject();
        }
        public string Snapshot() => App.Database.Snapshot(App.Database.Tables());
        public string BusinessSnapshot() => App.Database.Snapshot(new[] { "ledger_receipts", "ledger_receipt_groups", "ledger_receipt_details", "ledger_receipt_detail_people" });
        public void Role(string role, string? person = null) => App.Database.Execute($"UPDATE ledger_members SET role='{role}',person_id={(person is null ? "NULL" : "'" + person + "'")} WHERE ledger_id='{Ledger}' AND auth_user_id='{App.UserId}'");
        public async ValueTask DisposeAsync() { Client.Dispose(); await App.DisposeAsync(); }
    }
}
