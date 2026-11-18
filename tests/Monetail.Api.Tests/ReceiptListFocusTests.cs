using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Monetail.Api.Tests;

public sealed partial class ReceiptApiTests
{
    [Fact]
    public async Task ReceiptFocusStartsAtTargetAndContinuesWithOrdinaryCursorWithoutChangingData()
    {
        await using var f = await Fixture.Create();
        var ids = SeedList(f, 250, shop: "ＡＢＣ店舗");
        var query = "month=2026-11&ownerPersonId=" + D(f.Owner) + "&paymentMethodId=" + D(f.Payment) + "&q=abc";
        var before = f.Snapshot();
        var first = await ListPage(f, query + "&focusReceiptId=" + D(ids[149]));
        Keys(first, "receipts", "nextCursor");
        var rows = first["receipts"]!.AsArray();
        Assert.Equal(100, rows.Count);
        Keys(rows[0]!.AsObject(), "receiptId", "transactionAt", "shopName", "shopLocation", "paymentMethodId", "totalAmount", "memo");
        Assert.Equal(D(ids[149]), rows[0]!["receiptId"]!.GetValue<string>());
        var second = await ListPage(f, query + "&cursor=" + Uri.EscapeDataString(first["nextCursor"]!.GetValue<string>()));
        Assert.Equal(50, second["receipts"]!.AsArray().Count);
        Assert.Null(second["nextCursor"]);
        var found = rows.Concat(second["receipts"]!.AsArray()).Select(r => r!["receiptId"]!.GetValue<string>()).ToArray();
        Assert.Equal(ids.Take(150).Reverse().Select(D), found);
        Assert.Equal(150, found.Distinct().Count());
        Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task ReceiptFocusMissingTargetReturnsCommonNotFoundWithoutChangingData()
    {
        await using var f = await Fixture.Create();
        SeedList(f, 1);
        var before = f.Snapshot();
        var error = await ListPage(f, "month=all&focusReceiptId=" + Guid.NewGuid().ToString("D"), 404);
        Keys(error, "code", "message", "field", "latest", "permissions");
        Assert.Equal("not_found", error["code"]!.GetValue<string>());
        Assert.Null(error["latest"]);
        Assert.Equal(before, f.Snapshot());
    }
    [Theory]
    [InlineData(1)]
    [InlineData(26)]
    [InlineData(100)]
    [InlineData(101)]
    [InlineData(201)]
    public async Task ReceiptFocusIncludesEqualTimeTargetAtPageBoundariesAndAcceptsUppercaseD(int number)
    {
        await using var f = await Fixture.Create();
        var ids = SeedList(f, 201);
        var before = f.Snapshot();
        var page = await ListPage(f, "month=all&focusReceiptId=" + D(ids[number - 1]).ToUpperInvariant());
        var rows = page["receipts"]!.AsArray();
        Assert.Equal(Math.Min(100, number), rows.Count);
        Assert.Equal(ids.Take(number).Reverse().Take(100).Select(D), rows.Select(r => r!["receiptId"]!.GetValue<string>()));
        Assert.Equal(number > 100, page["nextCursor"] is not null);
        Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task ReceiptFocusUsesTransactionTimeBeforeIdAndNeverReturnsNewerTransfers()
    {
        await using var f = await Fixture.Create();
        var older = SeedList(f, 101, at: "2026-11-01T00:00:00", start: 1000);
        var newer = SeedList(f, 101, at: "2026-11-30T23:59:59");
        var before = f.Snapshot();
        var first = await ListPage(f, "month=2026-11&focusReceiptId=" + D(newer[0]));
        var second = await ListPage(f, "month=2026-11&cursor=" + Uri.EscapeDataString(first["nextCursor"]!.GetValue<string>()));
        Assert.Equal(new[] { D(newer[0]) }.Concat(older.Reverse().Select(D)), first["receipts"]!.AsArray().Concat(second["receipts"]!.AsArray()).Select(r => r!["receiptId"]!.GetValue<string>()));
        Assert.Null(second["nextCursor"]);
        Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("bad")]
    [InlineData("zero")]
    [InlineData("N")]
    [InlineData("braces")]
    [InlineData("whitespace")]
    [InlineData("duplicate")]
    [InlineData("cursor")]
    [InlineData("empty-cursor")]
    public async Task ReceiptFocusInvalidIdsDuplicatesAndCursorCombinationAreInputErrors(string kind)
    {
        await using var f = await Fixture.Create();
        var id = SeedList(f, 1)[0];
        var normal = D(id);
        var value = kind switch
        {
            "empty" => "",
            "bad" => "bad",
            "zero" => Guid.Empty.ToString("D"),
            "N" => id,
            "braces" => "{" + normal + "}",
            "whitespace" => " " + normal + " ",
            _ => normal
        };
        var query = "month=all&focusReceiptId=" + Uri.EscapeDataString(value);
        if (kind == "duplicate")
            query += "&focusReceiptId=" + Guid.NewGuid().ToString("D");
        if (kind == "cursor")
            query += "&cursor=broken";
        if (kind == "empty-cursor")
            query += "&cursor=";
        var before = f.Snapshot();
        var error = await ListPage(f, query, 400);
        Assert.Equal("invalid_input", error["code"]!.GetValue<string>());
        Assert.Equal("focusReceiptId", error["field"]!.GetValue<string>());
        Assert.Null(error["latest"]);
        Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("month")]
    [InlineData("owner")]
    [InlineData("payment")]
    [InlineData("name")]
    [InlineData("deleted")]
    public async Task ReceiptFocusOutOfScopeAndDeletedTargetAreIndistinguishableFromMissing(string kind)
    {
        await using var f = await Fixture.Create();
        var id = SeedList(f, 2, shop: "ＡＢＣ店舗")[1];
        var query = kind switch
        {
            "month" => "month=2026-12",
            "owner" => "month=all&ownerPersonId=" + Guid.NewGuid().ToString("D"),
            "payment" => "month=all&paymentMethodId=" + Guid.NewGuid().ToString("D"),
            "name" => "month=all&q=xyz",
            _ => "month=all"
        };
        if (kind == "deleted")
            f.App.Database.Execute($"DELETE FROM ledger_receipts WHERE receipt_id='{id}'");
        var before = f.Snapshot();
        var denied = await ListPage(f, query + "&focusReceiptId=" + D(id), 404);
        var missing = await ListPage(f, query + "&focusReceiptId=" + Guid.NewGuid().ToString("D"), 404);
        Assert.Equal("not_found", denied["code"]!.GetValue<string>());
        Assert.Equal(missing.ToJsonString(), denied.ToJsonString());
        Assert.False(denied.ContainsKey("receipts"));
        Assert.Null(denied["latest"]);
        Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task ReceiptFocusNeverFindsTargetInAnotherLedgerAndTransferApiRejectsReceiptFocus()
    {
        await using var f = await Fixture.Create();
        SeedList(f, 1);
        using var create = await f.Client.PostAsJsonAsync("/ledgers", new
        {
            requestId = Guid.NewGuid(),
            name = "別家計簿"
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var other = (await create.Content.ReadFromJsonAsync<JsonObject>())!["ledgerId"]!.GetValue<string>();
        f.App.Database.Execute($"INSERT INTO ledger_people(ledger_id,person_id,name,display_order,revision,payment_order_revision) SELECT '{other}',person_id,name,display_order,revision,payment_order_revision FROM ledger_people WHERE ledger_id='{f.Ledger}'");
        f.App.Database.Execute($"INSERT INTO ledger_payment_methods(ledger_id,payment_method_id,owner_person_id,name,display_order,revision) SELECT '{other}',payment_method_id,owner_person_id,name,display_order,revision FROM ledger_payment_methods WHERE ledger_id='{f.Ledger}'");
        var original = f.Ledger;
        f.Ledger = other;
        var foreign = SeedList(f, 1, start: 1000, shop: "他家計簿の非公開名")[0];
        f.Ledger = original;
        var before = f.Snapshot();
        var denied = await ListPage(f, "month=all&focusReceiptId=" + D(foreign), 404);
        var missing = await ListPage(f, "month=all&focusReceiptId=" + Guid.NewGuid().ToString("D"), 404);
        Assert.Equal(missing.ToJsonString(), denied.ToJsonString());
        var transfer = await TransferPage(f, "month=all&focusReceiptId=" + D(foreign), 400);
        Assert.Equal("invalid_input", transfer["code"]!.GetValue<string>());
        Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task ReceiptFocusKeepsExistingSharedReadLimit()
    {
        await using var f = await Fixture.Create();
        var id = SeedList(f, 1)[0];
        var before = f.Snapshot();
        for (var i = 0; i < 120; i++)
            await ListPage(f, "month=all&focusReceiptId=" + D(id));
        using var limited = await f.Client.GetAsync(f.Root + "?month=all&focusReceiptId=" + D(id));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        using var shared = await f.Client.GetAsync($"/ledgers/{D(f.Ledger)}/people");
        Assert.Equal(HttpStatusCode.TooManyRequests, shared.StatusCode);
        Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("anonymous")]
    [InlineData("expired")]
    [InlineData("removed")]
    public async Task ReceiptFocusChecksAuthenticationAndMembershipBeforeDisclosingTarget(string kind)
    {
        await using var f = await Fixture.Create();
        var id = SeedList(f, 1)[0];
        using var anonymous = f.App.Client();
        if (kind == "expired")
            f.App.Time.Advance(TimeSpan.FromDays(15));
        if (kind == "removed")
            f.App.Database.Execute("DELETE FROM ledger_members");
        var before = f.Snapshot();
        if (kind == "removed")
        {
            var denied = await ListPage(f, "month=all&focusReceiptId=" + D(id), 404);
            var missing = await ListPage(f, "month=all&focusReceiptId=" + Guid.NewGuid().ToString("D"), 404);
            Assert.Equal(denied.ToJsonString(), missing.ToJsonString());
        }
        else
        {
            using var response = await (kind == "anonymous" ? anonymous : f.Client).GetAsync(f.Root + "?month=all&focusReceiptId=" + D(id));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.DoesNotContain("receipts", await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(before, f.Snapshot());
    }

}
