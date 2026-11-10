using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Monetail.Api.Tests;

public sealed partial class ReceiptApiTests
{
    [Fact]
    public async Task PermissionIdentifiersAlsoUseDFormatAtReceiptBoundary()
    {
        await using var f = await Fixture.Create(); f.Role("Editor", f.Other);
        var error = (await f.Call("POST", f.Input(), 403))!;
        Assert.Equal(D(f.Other), error["permissions"]!["myPersonId"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("Admin", "GET", true, 200)] [InlineData("Editor", "GET", false, 200)] [InlineData("Viewer", "GET", false, 200)]
    [InlineData("Admin", "POST", false, 201)] [InlineData("Editor", "POST", true, 201)] [InlineData("Editor", "POST", false, 403)] [InlineData("Viewer", "POST", false, 403)]
    [InlineData("Admin", "PUT", false, 204)] [InlineData("Editor", "PUT", true, 204)] [InlineData("Editor", "PUT", false, 403)] [InlineData("Viewer", "PUT", false, 403)]
    [InlineData("Admin", "DELETE", false, 204)] [InlineData("Editor", "DELETE", true, 204)] [InlineData("Editor", "DELETE", false, 403)] [InlineData("Viewer", "DELETE", false, 403)]
    public async Task LedgerRolesAndOwnerMappingAreAuthoritative(string role, string method, bool mapped, int status)
    {
        await using var f = await Fixture.Create(); var id = await f.CreateReceipt(f.Input()); var row = await f.Get(id);
        f.Role(role, mapped ? f.Owner : null); var before = f.Snapshot();
        var body = method == "POST" ? f.Input() : method == "PUT" ? f.Update(row) : new JsonObject { ["requestId"] = Guid.NewGuid().ToString("D"), ["revision"] = row["revision"]!.GetValue<string>() };
        await f.Call(method, method == "GET" ? null : body, status, method == "POST" ? "" : "/" + id);
        if (status == 403 || method == "GET") Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task EditorMustOwnBothOldAndNewPaymentMethods(bool ownOld)
    {
        await using var f = await Fixture.Create(); var initial = f.Input();
        if (!ownOld) initial["paymentMethodId"] = D(f.OtherPayment);
        var id = await f.CreateReceipt(initial); var input = f.Update(await f.Get(id));
        input["paymentMethodId"] = D(ownOld ? f.OtherPayment : f.Payment); f.Role("Editor", f.Owner); var before = f.Snapshot();
        await f.Call("PUT", input, 403, "/" + id); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task RepeatedExistingChildIdsAreInputErrorsNotMissingResources(bool group)
    {
        await using var f = await Fixture.Create(); var id = await f.CreateReceipt(f.Input()); var input = f.Update(await f.Get(id));
        if (group)
        {
            var copy = Group(input).DeepClone().AsObject(); copy["categoryId"] = D(f.OtherCategory); input["groups"]!.AsArray().Add(copy);
        }
        else Group(input)["details"]!.AsArray().Add(Line(input).DeepClone());
        var before = f.Snapshot(); var error = (await f.Call("PUT", input, 400, "/" + id))!;
        Assert.Equal(group ? "groups[1].groupId" : "groups[0].details[1].detailId", error["field"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task SnapshotUpdateAddsDeletesMovesAndPreservesExistingChildIds()
    {
        await using var f = await Fixture.Create(); var input = f.Input();
        var second = Group(input).DeepClone().AsObject(); second["categoryId"] = D(f.OtherCategory);
        second["details"]![0]!["unitPrice"] = 200; input["groups"]!.AsArray().Add(second);
        var id = await f.CreateReceipt(input); var old = await f.Get(id);
        var update = f.Update(old); var moving = Line(update).DeepClone(); var groupB = Group(update, 1).DeepClone().AsObject();
        groupB["details"] = new JsonArray(moving); update["groups"] = new JsonArray(groupB);
        var masters = f.App.Database.Snapshot(["ledgers", "ledger_people", "ledger_payment_methods", "ledger_categories"]);
        await f.Call("PUT", update, 204, "/" + id); var result = await f.Get(id);
        Assert.Single(result["groups"]!.AsArray()); Assert.Equal(Line(old)["detailId"]!.GetValue<string>(), Line(result)["detailId"]!.GetValue<string>());
        Assert.Equal(Group(old, 1)["groupId"]!.GetValue<string>(), Group(result)["groupId"]!.GetValue<string>());
        Assert.Equal(masters, f.App.Database.Snapshot(["ledgers", "ledger_people", "ledger_payment_methods", "ledger_categories"]));
        update = f.Update(result); var extra = Line(update).DeepClone().AsObject(); extra.Remove("detailId"); extra["name"] = "追加明細";
        Group(update)["details"]!.AsArray().Add(extra); await f.Call("PUT", update, 204, "/" + id); result = await f.Get(id);
        Assert.Equal(2, Group(result)["details"]!.AsArray().Count); AssertD(Line(result, index: 1)["detailId"]!, '7');
    }

    [Theory]
    [InlineData("POST")] [InlineData("PUT")] [InlineData("DELETE")]
    public async Task ReplaysAreExactAndDifferentContentOrOperationConflicts(string method)
    {
        await using var f = await Fixture.Create(); var input = f.Input(); string id;
        if (method == "POST") id = await f.CreateReceipt(input);
        else
        {
            id = await f.CreateReceipt(input); var row = await f.Get(id);
            input = method == "PUT" ? f.Update(row) : new JsonObject { ["requestId"] = Guid.NewGuid().ToString("D"), ["revision"] = row["revision"]!.GetValue<string>() };
            if (method == "PUT") input["memo"] = "更新";
            await f.Call(method, input, 204, "/" + id);
        }
        var before = f.Snapshot(); await f.Call(method, input, method == "POST" ? 201 : 204, method == "POST" ? "" : "/" + id); Assert.Equal(before, f.Snapshot());
        var changed = input.DeepClone().AsObject();
        if (method == "DELETE") changed["revision"] = Guid.NewGuid().ToString("D"); else changed["shopName"] = "異なる要求";
        var conflict = await f.Call(method, changed, 409, method == "POST" ? "" : "/" + id); Assert.Equal("request_conflict", conflict!["code"]!.GetValue<string>());
        var create = f.Input(); create["requestId"] = input["requestId"]!.GetValue<string>();
        if (method != "POST") Assert.Equal("request_conflict", (await f.Call("POST", create, 409))!["code"]!.GetValue<string>());
        Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task ReplayAfterTargetDeletionReturnsPriorPostAndPutStatus()
    {
        await using var f = await Fixture.Create(); var create = f.Input(); var id = await f.CreateReceipt(create); var put = f.Update(await f.Get(id));
        put["memo"] = "更新"; await f.Call("PUT", put, 204, "/" + id); var latest = await f.Get(id);
        await f.Call("DELETE", new JsonObject { ["requestId"] = Guid.NewGuid().ToString("D"), ["revision"] = latest["revision"]!.GetValue<string>() }, 204, "/" + id);
        var before = f.Snapshot(); await f.Call("POST", create, 201); await f.Call("PUT", put, 204, "/" + id); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("Admin", 204)] [InlineData("Editor", 204)] [InlineData("Viewer", 403)] [InlineData("removed", 404)]
    public async Task DeleteReplayUsesCurrentRoleWithoutOldOwnerMapping(string role, int status)
    {
        await using var f = await Fixture.Create(); var id = await f.CreateReceipt(f.Input()); var row = await f.Get(id);
        var request = new JsonObject { ["requestId"] = Guid.NewGuid().ToString("D"), ["revision"] = row["revision"]!.GetValue<string>() };
        await f.Call("DELETE", request, 204, "/" + id);
        if (role == "removed") f.App.Database.Execute("DELETE FROM ledger_members"); else f.Role(role);
        var before = f.Snapshot(); await f.Call("DELETE", request, status, "/" + id); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("PUT")] [InlineData("DELETE")]
    public async Task StaleRevisionReturnsExactFullLatestWithoutChangingData(string method)
    {
        await using var f = await Fixture.Create(); var id = await f.CreateReceipt(f.Input()); var old = await f.Get(id);
        var first = f.Update(old); first["memo"] = "最新"; await f.Call("PUT", first, 204, "/" + id); var latest = await f.Get(id); var before = f.Snapshot();
        var stale = method == "PUT" ? f.Update(old) : new JsonObject { ["requestId"] = Guid.NewGuid().ToString("D"), ["revision"] = old["revision"]!.GetValue<string>() };
        var conflict = (await f.Call(method, stale, 409, "/" + id))!;
        Assert.Equal("revision_conflict", conflict["code"]!.GetValue<string>()); Assert.True(JsonNode.DeepEquals(latest, conflict["latest"]));
        Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task ConcurrentDistinctRequestsCannotOverwriteAStaleRevision()
    {
        await using var f = await Fixture.Create(); var id = await f.CreateReceipt(f.Input()); var row = await f.Get(id);
        var a = f.Update(row); a["memo"] = "A"; var b = f.Update(row); b["memo"] = "B";
        var responses = await Task.WhenAll(f.Client.PutAsJsonAsync(f.Root + "/" + id, a), f.Client.PutAsJsonAsync(f.Root + "/" + id, b));
        try { Assert.Equal(new[] { 204, 409 }, responses.Select(r => (int)r.StatusCode).Order()); }
        finally { foreach (var response in responses) response.Dispose(); }
        Assert.Equal(1L, f.App.Database.Scalar("SELECT COUNT(*) FROM operation_requests WHERE operation_type='receipt.update'"));
    }

    [Fact]
    public async Task ConcurrentSameCreateRequestCommitsOnlyOneTree()
    {
        await using var f = await Fixture.Create(); var input = f.Input();
        var responses = await Task.WhenAll(f.Client.PostAsJsonAsync(f.Root, input), f.Client.PostAsJsonAsync(f.Root, input));
        try { Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode)); }
        finally { foreach (var response in responses) response.Dispose(); }
        Assert.Equal(1L, f.App.Database.Scalar("SELECT COUNT(*) FROM ledger_receipts"));
        Assert.Equal(1L, f.App.Database.Scalar("SELECT COUNT(*) FROM operation_requests WHERE operation_type='receipt.create'"));
    }

    [Theory]
    [InlineData("category")] [InlineData("payment")] [InlineData("person")] [InlineData("receipt")] [InlineData("group")] [InlineData("detail")]
    public async Task ForeignLedgerAndForeignReceiptIdentifiersAreNeverReparented(string kind)
    {
        await using var f = await Fixture.Create(); var first = await f.CreateReceipt(f.Input()); var second = await f.CreateReceipt(f.Input());
        var row = await f.Get(first); var other = await f.Get(second); var input = f.Update(row);
        if (kind == "group") Group(input)["groupId"] = Group(other)["groupId"]!.GetValue<string>();
        else if (kind == "detail") Line(input)["detailId"] = Line(other)["detailId"]!.GetValue<string>();
        else if (kind == "category") Group(input)["categoryId"] = Guid.NewGuid().ToString("D");
        else if (kind == "payment") input["paymentMethodId"] = Guid.NewGuid().ToString("D");
        else if (kind == "person") Line(input)["personIds"] = new JsonArray(Guid.NewGuid().ToString("D"));
        var before = f.Snapshot(); await f.Call("PUT", input, 404, "/" + (kind == "receipt" ? Guid.NewGuid().ToString("D") : first)); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("requestId")] [InlineData("revision")] [InlineData("nullableMissing")] [InlineData("extra")] [InlineData("nullChildId")]
    [InlineData("quantity")] [InlineData("fraction")] [InlineData("rateNumber")] [InlineData("rateUnknown")] [InlineData("date")]
    [InlineData("peopleDuplicate")] [InlineData("groupsEmpty")] [InlineData("detailsEmpty")] [InlineData("overflow")]
    public async Task InvalidInputNeverWritesOrAddsReplayRecords(string kind)
    {
        await using var f = await Fixture.Create(); var input = f.Input();
        switch (kind)
        {
            case "requestId": input["requestId"] = Guid.NewGuid().ToString("N"); break;
            case "revision": input["revision"] = Guid.NewGuid().ToString("D"); break;
            case "nullableMissing": input.Remove("shopLocation"); break;
            case "extra": input["totalAmount"] = 1; break;
            case "nullChildId": Line(input)["detailId"] = null; break;
            case "quantity": Line(input)["quantity"] = 0; break;
            case "fraction": Line(input)["quantity"] = 1.5; break;
            case "rateNumber": Line(input)["discountRate"] = 1; break;
            case "rateUnknown": Line(input)["taxRate"] = "8"; break;
            case "date": input["transactionAt"] = "2026-02-30T12:00:00"; break;
            case "peopleDuplicate": Line(input)["personIds"] = new JsonArray(D(f.Owner), D(f.Owner)); break;
            case "groupsEmpty": input["groups"] = new JsonArray(); break;
            case "detailsEmpty": Group(input)["details"] = new JsonArray(); break;
            default: Line(input)["unitPrice"] = int.MaxValue; break;
        }
        var before = f.Snapshot(); var error = (await f.Call("POST", input, 400))!;
        Assert.Equal("invalid_input", error["code"]!.GetValue<string>()); Assert.NotNull(error["field"]); Assert.False(error.ContainsKey("reason")); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("anonymous", 401)] [InlineData("csrf", 400)] [InlineData("origin", 403)] [InlineData("member", 404)]
    public async Task ExistingAuthenticationAndCsrfBoundariesAreMaintained(string kind, int status)
    {
        await using var f = await Fixture.Create(); using var anonymous = f.App.Client(); var client = kind == "anonymous" ? anonymous : f.Client;
        if (kind == "csrf") client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        if (kind == "origin") client.DefaultRequestHeaders.Remove("Origin");
        if (kind == "member") f.App.Database.Execute("DELETE FROM ledger_members");
        var before = f.Snapshot(); using var response = await client.PostAsJsonAsync(f.Root, f.Input()); Assert.Equal(status, (int)response.StatusCode); Assert.Equal(before, f.Snapshot());
    }
}
