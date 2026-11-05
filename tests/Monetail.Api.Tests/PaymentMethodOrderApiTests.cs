using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Monetail.Api.Tests;

public sealed partial class PaymentMethodApiTests
{
    private static string PaymentId(JsonElement row) => row.GetProperty("paymentMethodId").GetString()!;
    private static Task<JsonElement> Order(Fixture f, string[] ids, string? revision = null, int status = 200, string? owner = null) =>
        f.Call("PUT", new { ownerPersonId = owner ?? f.Owner, revision = revision ?? f.OwnerVersion(owner), paymentMethodIds = ids }, status, "/order");

    [Fact]
    public async Task OrderRenumbersAllDatesAndKeepsOtherVersionsIndependent()
    {
        await using var f = await Fixture.Create();
        var first = await f.Payment("現金"); var second = await f.Payment("カード");
        await f.Call("PATCH", new { revision = Revision(first), validTo = "2000-01-01" }, suffix: Path(first));
        await f.Call("POST", new { requestId = Guid.NewGuid(), ownerPersonId = f.Other, name = "他人" }, 201);
        f.App.Database.Execute($"UPDATE ledger_payment_methods SET display_order=display_order+7 WHERE owner_person_id='{f.Owner}'");
        f.App.Database.Execute($"UPDATE ledger_payment_methods SET display_order=9 WHERE owner_person_id='{f.Other}'");
        var before = f.OwnerVersion(); var other = f.OwnerVersion(f.Other);
        var personData = f.App.Database.Scalar("SELECT group_concat(revision || ':' || payment_order_revision) FROM ledger_people WHERE person_id='" + f.Other + "'");
        var personRevision = f.App.Database.Scalar($"SELECT revision FROM ledger_people WHERE person_id='{f.Owner}'");
        var peopleOrder = f.App.Database.Scalar($"SELECT people_order_revision FROM ledgers WHERE id='{f.Ledger}'");
        var paymentData = f.App.Database.Scalar("SELECT group_concat(payment_method_id || ':' || revision || ':' || name || ':' || coalesce(valid_to,'')) FROM ledger_payment_methods");
        var result = await Order(f, [PaymentId(second), PaymentId(first)]);
        var rows = result.GetProperty("paymentMethods").EnumerateArray().Where(r => r.GetProperty("ownerPersonId").GetString() == f.Owner).ToArray();
        Assert.Equal(new[] { PaymentId(second), PaymentId(first) }, rows.Select(PaymentId));
        Assert.Equal(new[] { 0, 1 }, rows.Select(r => r.GetProperty("displayOrder").GetInt32()));
        Assert.Equal("2000-01-01", rows[1].GetProperty("validTo").GetString());
        Assert.NotEqual(before, f.OwnerVersion()); Assert.Equal(other, f.OwnerVersion(f.Other));
        Assert.Equal(9, result.GetProperty("paymentMethods").EnumerateArray().Single(r => r.GetProperty("ownerPersonId").GetString() == f.Other).GetProperty("displayOrder").GetInt32());
        Assert.Equal(personRevision, f.App.Database.Scalar($"SELECT revision FROM ledger_people WHERE person_id='{f.Owner}'"));
        Assert.Equal(peopleOrder, f.App.Database.Scalar($"SELECT people_order_revision FROM ledgers WHERE id='{f.Ledger}'"));
        Assert.Equal(personData, f.App.Database.Scalar("SELECT group_concat(revision || ':' || payment_order_revision) FROM ledger_people WHERE person_id='" + f.Other + "'"));
        Assert.Equal(paymentData, f.App.Database.Scalar("SELECT group_concat(payment_method_id || ':' || revision || ':' || name || ':' || coalesce(valid_to,'')) FROM ledger_payment_methods"));
        Assert.Equal(f.OwnerVersion(), result.GetProperty("ownerOrderRevisions").EnumerateArray().Single(p => p.GetProperty("personId").GetString() == f.Owner).GetProperty("orderRevision").GetString());
    }

    [Theory]
    [InlineData("Admin", false, 200)] [InlineData("Editor", true, 200)]
    [InlineData("Editor", false, 403)] [InlineData("Viewer", true, 403)]
    public async Task OrderPermissions(string role, bool own, int status)
    {
        await using var f = await Fixture.Create(); var row = await f.Payment(); f.Role(role, own); var before = f.Snapshot();
        await Order(f, [PaymentId(row)], status: status);
        if (status != 200) Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(3)]
    public async Task OrderSameSequenceIncludingEmptyAlwaysAdvancesOnlyOrderVersion(int count)
    {
        await using var f = await Fixture.Create(); var ids = new List<string>();
        for (var i = 0; i < count; i++) ids.Add(PaymentId(await f.Payment("方法" + i)));
        var previous = f.OwnerVersion(); var other = f.OwnerVersion(f.Other);
        await Order(f, ids.ToArray()); var first = f.OwnerVersion();
        Assert.NotEqual(previous, first); Assert.NotEqual(Guid.Empty, Guid.Parse(first));
        await Order(f, ids.ToArray()); Assert.NotEqual(first, f.OwnerVersion()); Assert.Equal(other, f.OwnerVersion(f.Other));
        var before = f.Snapshot(); var stale = await Order(f, ids.ToArray(), first, 409);
        AssertOrderLatest(stale, f.Owner, f.OwnerVersion(), ids.ToArray()); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("duplicate")] [InlineData("normalizedDuplicate")] [InlineData("missing")]
    [InlineData("otherOwner")] [InlineData("otherLedger")] [InlineData("unknown")]
    [InlineData("uuid")] [InlineData("emptyUuid")] [InlineData("number")]
    [InlineData("null")] [InlineData("object")] [InlineData("absent")]
    public async Task OrderRejectsInvalidFullCollectionWithoutWrites(string kind)
    {
        await using var f = await Fixture.Create(); var first = await f.Payment(); var second = await f.Payment("カード");
        var a = PaymentId(first); var b = PaymentId(second); var revision = f.OwnerVersion();
        var other = await f.Call("POST", new { requestId = Guid.NewGuid(), ownerPersonId = f.Other, name = "他人" }, 201);
        var foreign = Guid.NewGuid().ToString("N");
        if (kind == "otherLedger")
        {
            var ledger = (await Send(f.Client, "POST", "/ledgers", new { requestId = Guid.NewGuid(), name = "別家計簿" }, 201)).GetProperty("ledgerId").GetString();
            var people = await Send(f.Client, "POST", $"/ledgers/{ledger}/people", new { requestId = Guid.NewGuid(), name = "別人物" }, 201);
            var person = people.GetProperty("people")[0].GetProperty("personId").GetString();
            foreign = (await Send(f.Client, "POST", $"/ledgers/{ledger}/payment-methods", new { requestId = Guid.NewGuid(), ownerPersonId = person, name = "別方法" }, 201)).GetProperty("paymentMethodId").GetString()!;
        }
        object? ids = kind switch {
            "duplicate" => new[] { a, a }, "normalizedDuplicate" => new[] { a, Guid.Parse(a).ToString("D").ToUpperInvariant() },
            "missing" => new[] { a }, "otherOwner" => new[] { a, other.GetProperty("paymentMethodId").GetString() },
            "otherLedger" => new[] { a, foreign }, "unknown" => new[] { a, Guid.NewGuid().ToString() },
            "uuid" => new[] { a, "bad" }, "emptyUuid" => new[] { a, Guid.Empty.ToString() }, "number" => new object[] { a, 1 },
            "null" => null, "object" => new { a, b }, _ => new[] { a, b }
        };
        var before = f.Snapshot();
        object input = kind == "absent" ? new { ownerPersonId = f.Owner, revision } : new { ownerPersonId = f.Owner, revision, paymentMethodIds = ids };
        var result = await f.Call("PUT", input, 400, "/order"); Assert.Equal("invalid_input", result.GetProperty("code").GetString()); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task OrderUnknownOrForeignOwnerIsNotFound(bool foreign)
    {
        await using var f = await Fixture.Create(); var owner = Guid.NewGuid().ToString();
        if (foreign)
        {
            var ledger = (await Send(f.Client, "POST", "/ledgers", new { requestId = Guid.NewGuid(), name = "別" }, 201)).GetProperty("ledgerId").GetString();
            owner = (await Send(f.Client, "POST", $"/ledgers/{ledger}/people", new { requestId = Guid.NewGuid(), name = "別人" }, 201)).GetProperty("people")[0].GetProperty("personId").GetString()!;
        }
        var before = f.Snapshot(); await Order(f, [], Guid.NewGuid().ToString(), 404, owner); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task OrderStaleVersionReturnsOnlyTargetLatestIncludingExpiredAndAddedRows()
    {
        await using var f = await Fixture.Create(); var first = await f.Payment(); var old = f.OwnerVersion(); var second = await f.Payment("追加");
        await f.Call("PATCH", new { revision = Revision(first), validTo = "2000-01-01" }, suffix: Path(first));
        var before = f.Snapshot(); var conflict = await Order(f, [PaymentId(first)], old, 409);
        AssertOrderLatest(conflict, f.Owner, f.OwnerVersion(), [PaymentId(first), PaymentId(second)]); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task OrderConcurrentOldVersionOnlyOneSucceeds()
    {
        await using var f = await Fixture.Create(); var a = PaymentId(await f.Payment()); var b = PaymentId(await f.Payment("カード")); var revision = f.OwnerVersion();
        var inputs = new[] { new[] { a, b }, new[] { b, a } };
        var responses = await Task.WhenAll(inputs.Select(ids => f.Client.PutAsJsonAsync(f.Root + "/order", new { ownerPersonId = f.Owner, revision, paymentMethodIds = ids })));
        try
        {
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
            var winner = Array.FindIndex(responses, r => r.StatusCode == HttpStatusCode.OK);
            var conflict = await responses[1 - winner].Content.ReadFromJsonAsync<JsonElement>();
            AssertOrderLatest(conflict, f.Owner, f.OwnerVersion(), inputs[winner]);
            var rows = (await f.Call("GET")).GetProperty("paymentMethods").EnumerateArray().ToArray();
            Assert.Equal(inputs[winner], rows.Select(PaymentId));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task OrderDbConditionRejectsWriteEvenWhenLoadedVersionMatched()
    {
        await using var f = await Fixture.Create(); var row = await f.Payment();
        f.App.Database.Execute("CREATE TRIGGER stop_order BEFORE UPDATE OF payment_order_revision ON ledger_people BEGIN SELECT RAISE(IGNORE); END");
        var before = f.Snapshot(); var result = await Order(f, [PaymentId(row)], status: 409);
        AssertOrderLatest(result, f.Owner, f.OwnerVersion(), [PaymentId(row)]); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task OrderFailureDuringSecondRowRollsBackEarlierRowAndVersion()
    {
        await using var f = await Fixture.Create(); var a = PaymentId(await f.Payment()); var b = PaymentId(await f.Payment("カード"));
        f.App.Database.Execute("CREATE TABLE test_order_writes(value INTEGER); CREATE TRIGGER fail_order BEFORE UPDATE OF display_order ON ledger_payment_methods BEGIN INSERT INTO test_order_writes VALUES(1); SELECT CASE WHEN (SELECT COUNT(*) FROM test_order_writes)=2 THEN RAISE(ABORT,'synthetic') END; END");
        var before = f.Snapshot(); await Order(f, [b, a], status: 503); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("role")] [InlineData("person")] [InlineData("session")] [InlineData("membership")]
    public async Task OrderFinalAuthorizationRecheckRollsBackChanges(string change)
    {
        await using var f = await Fixture.Create(); var a = PaymentId(await f.Payment()); var b = PaymentId(await f.Payment("カード")); f.Role("Editor", true);
        var mutation = change switch { "role" => "UPDATE ledger_members SET role='Viewer'", "person" => $"UPDATE ledger_members SET person_id='{f.Other}'", "session" => "UPDATE auth_sessions SET RevokedAtUtc=CreatedAtUtc", _ => "DELETE FROM ledger_members" };
        f.App.Database.Execute($"CREATE TRIGGER change_actor AFTER UPDATE OF payment_order_revision ON ledger_people BEGIN {mutation}; END");
        var before = f.Snapshot(); await Order(f, [b, a], status: change == "session" ? 401 : change == "membership" ? 404 : 403); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("anonymous", 401)] [InlineData("csrf", 400)] [InlineData("origin", 403)] [InlineData("notMember", 404)]
    public async Task OrderAuthenticationBoundaryDoesNotWrite(string kind, int status)
    {
        await using var f = await Fixture.Create(); using var anonymous = f.App.Client();
        var revision = f.OwnerVersion(); var client = kind == "anonymous" ? anonymous : f.Client;
        if (kind == "csrf") client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        if (kind == "origin") client.DefaultRequestHeaders.Remove("Origin");
        if (kind == "notMember") f.App.Database.Execute("DELETE FROM ledger_members");
        var before = f.Snapshot(); using var response = await client.PutAsJsonAsync(f.Root + "/order", new { ownerPersonId = f.Owner, revision, paymentMethodIds = Array.Empty<string>() });
        Assert.Equal((HttpStatusCode)status, response.StatusCode); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task OrderEditorCannotReorderAnotherMappedPerson()
    {
        await using var f = await Fixture.Create(); var row = await f.Payment();
        f.Role("Editor", false); f.App.Database.Execute($"UPDATE ledger_members SET person_id='{f.Other}' WHERE ledger_id='{f.Ledger}'");
        var before = f.Snapshot(); var result = await Order(f, [PaymentId(row)], status: 403);
        Assert.Equal(JsonValueKind.Null, result.GetProperty("latest").ValueKind); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task OrderDifferentOwnersCanSaveWithIndependentVersions()
    {
        await using var f = await Fixture.Create(); var a = PaymentId(await f.Payment()); var b = PaymentId(await f.Payment("カード"));
        var otherIds = new List<string>();
        foreach (var name in new[] { "他の現金", "他のカード" })
            otherIds.Add((await f.Call("POST", new { requestId = Guid.NewGuid(), ownerPersonId = f.Other, name }, 201)).GetProperty("paymentMethodId").GetString()!);
        var ownVersion = f.OwnerVersion(); var otherVersion = f.OwnerVersion(f.Other);
        await Task.WhenAll(Order(f, [b, a], ownVersion), Order(f, otherIds.AsEnumerable().Reverse().ToArray(), otherVersion, owner: f.Other));
        Assert.NotEqual(ownVersion, f.OwnerVersion()); Assert.NotEqual(otherVersion, f.OwnerVersion(f.Other));
        var rows = (await f.Call("GET")).GetProperty("paymentMethods").EnumerateArray().ToArray();
        Assert.Equal(new[] { b, a }, rows.Where(r => r.GetProperty("ownerPersonId").GetString() == f.Owner).Select(PaymentId));
        Assert.Equal(otherIds.AsEnumerable().Reverse(), rows.Where(r => r.GetProperty("ownerPersonId").GetString() == f.Other).Select(PaymentId));
    }

    [Theory]
    [InlineData("owner")] [InlineData("revision")] [InlineData("extra")]
    public async Task OrderRejectsInvalidEnvelope(string kind)
    {
        await using var f = await Fixture.Create(); var before = f.Snapshot();
        object input = kind switch {
            "owner" => new { ownerPersonId = "bad", revision = f.OwnerVersion(), paymentMethodIds = Array.Empty<string>() },
            "revision" => new { ownerPersonId = f.Owner, revision = "bad", paymentMethodIds = Array.Empty<string>() },
            _ => new { ownerPersonId = f.Owner, revision = f.OwnerVersion(), paymentMethodIds = Array.Empty<string>(), displayOrder = 0 }
        };
        await f.Call("PUT", input, 400, "/order"); Assert.Equal(before, f.Snapshot());
    }

    private static void AssertOrderLatest(JsonElement result, string owner, string revision, string[] ids)
    {
        Assert.Equal("revision_conflict", result.GetProperty("code").GetString()); var latest = result.GetProperty("latest");
        Assert.Equal(owner, latest.GetProperty("ownerPersonId").GetString()); Assert.Equal(revision, latest.GetProperty("orderRevision").GetString());
        Assert.Equal(ids, latest.GetProperty("paymentMethodIds").EnumerateArray().Select(id => id.GetString()));
    }
}
