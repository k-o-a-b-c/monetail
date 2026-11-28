using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;
using static Monetail.Api.Tests.RecordApiTestData;

namespace Monetail.Api.Tests;

public sealed partial class TransferListApiTests
{
    [Fact]
    public async Task TransferFocusStartsAtTargetAndContinuesWithOrdinaryCursorWithoutChangingData()
    {
        await using var f = await RecordApiFixture.Create(); var ids = SeedTransferList(f, 250, name: "ＡＢＣ振替");
        var query = "month=2026-11&ownerPersonId=" + D(f.Owner) + "&paymentMethodId=" + D(f.OtherPayment) + "&q=abc";
        var before = f.Snapshot(); var first = await TransferPage(f, query + "&focusTransferId=" + D(ids[149]));
        Keys(first, "transfers", "nextCursor"); var rows = first["transfers"]!.AsArray(); Assert.Equal(100, rows.Count);
        Keys(rows[0]!.AsObject(), "transferId", "transactionAt", "name", "fromPaymentMethodId", "toPaymentMethodId", "amount", "memo");
        Assert.Equal(D(ids[149]), rows[0]!["transferId"]!.GetValue<string>());
        var second = await TransferPage(f, query + "&cursor=" + Uri.EscapeDataString(first["nextCursor"]!.GetValue<string>()));
        Assert.Equal(50, second["transfers"]!.AsArray().Count); Assert.Null(second["nextCursor"]);
        var found = rows.Concat(second["transfers"]!.AsArray()).Select(r => r!["transferId"]!.GetValue<string>()).ToArray();
        Assert.Equal(ids.Take(150).Reverse().Select(D), found); Assert.Equal(150, found.Distinct().Count()); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferFocusMissingTargetReturnsCommonNotFoundWithoutChangingData()
    {
        await using var f = await RecordApiFixture.Create(); SeedTransferList(f, 1); var before = f.Snapshot();
        var error = await TransferPage(f, "month=all&focusTransferId=" + Guid.NewGuid().ToString("D"), 404);
        Keys(error, "code", "message", "field", "latest", "permissions"); Assert.Equal("not_found", error["code"]!.GetValue<string>());
        Assert.Null(error["latest"]); Assert.Equal(before, f.Snapshot());
    }
    [Theory]
    [InlineData(1)] [InlineData(26)] [InlineData(100)] [InlineData(101)] [InlineData(201)]
    public async Task TransferFocusIncludesEqualTimeTargetAtPageBoundariesAndAcceptsUppercaseD(int number)
    {
        await using var f = await RecordApiFixture.Create(); var ids = SeedTransferList(f, 201); var before = f.Snapshot();
        var page = await TransferPage(f, "month=all&focusTransferId=" + D(ids[number - 1]).ToUpperInvariant());
        var rows = page["transfers"]!.AsArray(); Assert.Equal(Math.Min(100, number), rows.Count);
        Assert.Equal(ids.Take(number).Reverse().Take(100).Select(D), rows.Select(r => r!["transferId"]!.GetValue<string>()));
        Assert.Equal(number > 100, page["nextCursor"] is not null); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferFocusUsesTransactionTimeBeforeIdAndNeverReturnsNewerTransfers()
    {
        await using var f = await RecordApiFixture.Create(); var older = SeedTransferList(f, 101, at: "2026-11-01T00:00:00", start: 1000);
        var newer = SeedTransferList(f, 101, at: "2026-11-30T23:59:59"); var before = f.Snapshot();
        var first = await TransferPage(f, "month=2026-11&focusTransferId=" + D(newer[0]));
        var second = await TransferPage(f, "month=2026-11&cursor=" + Uri.EscapeDataString(first["nextCursor"]!.GetValue<string>()));
        Assert.Equal(new[] { D(newer[0]) }.Concat(older.Reverse().Select(D)), first["transfers"]!.AsArray().Concat(second["transfers"]!.AsArray()).Select(r => r!["transferId"]!.GetValue<string>()));
        Assert.Null(second["nextCursor"]); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("empty")] [InlineData("bad")] [InlineData("zero")] [InlineData("N")] [InlineData("braces")]
    [InlineData("whitespace")] [InlineData("duplicate")] [InlineData("cursor")] [InlineData("empty-cursor")]
    public async Task TransferFocusInvalidIdsDuplicatesAndCursorCombinationAreInputErrors(string kind)
    {
        await using var f = await RecordApiFixture.Create(); var id = SeedTransferList(f, 1)[0]; var normal = D(id);
        var value = kind switch { "empty" => "", "bad" => "bad", "zero" => Guid.Empty.ToString("D"), "N" => id, "braces" => "{" + normal + "}", "whitespace" => " " + normal + " ", _ => normal };
        var query = "month=all&focusTransferId=" + Uri.EscapeDataString(value);
        if (kind == "duplicate") query += "&focusTransferId=" + Guid.NewGuid().ToString("D");
        if (kind == "cursor") query += "&cursor=broken";
        if (kind == "empty-cursor") query += "&cursor=";
        var before = f.Snapshot(); var error = await TransferPage(f, query, 400);
        Assert.Equal("invalid_input", error["code"]!.GetValue<string>()); Assert.Equal("focusTransferId", error["field"]!.GetValue<string>());
        Assert.Null(error["latest"]); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("month")] [InlineData("owner")] [InlineData("payment")] [InlineData("name")] [InlineData("deleted")]
    public async Task TransferFocusOutOfScopeAndDeletedTargetAreIndistinguishableFromMissing(string kind)
    {
        await using var f = await RecordApiFixture.Create(); var id = SeedTransferList(f, 2, name: "ＡＢＣ振替")[1];
        var query = kind switch { "month" => "month=2026-12", "owner" => "month=all&ownerPersonId=" + Guid.NewGuid().ToString("D"),
            "payment" => "month=all&paymentMethodId=" + Guid.NewGuid().ToString("D"), "name" => "month=all&q=xyz", _ => "month=all" };
        if (kind == "deleted") f.App.Database.Execute($"DELETE FROM ledger_transfers WHERE transfer_id='{id}'");
        var before = f.Snapshot(); var denied = await TransferPage(f, query + "&focusTransferId=" + D(id), 404);
        var missing = await TransferPage(f, query + "&focusTransferId=" + Guid.NewGuid().ToString("D"), 404);
        Assert.Equal("not_found", denied["code"]!.GetValue<string>()); Assert.Equal(missing.ToJsonString(), denied.ToJsonString());
        Assert.False(denied.ContainsKey("transfers")); Assert.Null(denied["latest"]); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferFocusNeverFindsTargetInAnotherLedgerAndReceiptApiStillRejectsFocus()
    {
        await using var f = await RecordApiFixture.Create(); SeedTransferList(f, 1);
        using var create = await f.Client.PostAsJsonAsync("/ledgers", new { requestId = Guid.NewGuid(), name = "別家計簿" }); Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var other = (await create.Content.ReadFromJsonAsync<JsonObject>())!["ledgerId"]!.GetValue<string>();
        f.App.Database.Execute($"INSERT INTO ledger_people(ledger_id,person_id,name,display_order,revision,payment_order_revision) SELECT '{other}',person_id,name,display_order,revision,payment_order_revision FROM ledger_people WHERE ledger_id='{f.Ledger}'");
        f.App.Database.Execute($"INSERT INTO ledger_payment_methods(ledger_id,payment_method_id,owner_person_id,name,display_order,revision) SELECT '{other}',payment_method_id,owner_person_id,name,display_order,revision FROM ledger_payment_methods WHERE ledger_id='{f.Ledger}'");
        var foreign = SeedTransferList(f, 1, ledger: other, start: 1000, name: "他家計簿の非公開名")[0]; var before = f.Snapshot();
        var denied = await TransferPage(f, "month=all&focusTransferId=" + D(foreign), 404);
        var missing = await TransferPage(f, "month=all&focusTransferId=" + Guid.NewGuid().ToString("D"), 404);
        Assert.Equal(missing.ToJsonString(), denied.ToJsonString());
        var receipt = await ListPage(f, "month=all&focusTransferId=" + D(foreign), 400); Assert.Equal("invalid_input", receipt["code"]!.GetValue<string>());
        Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferFocusKeepsExistingSharedReadLimit()
    {
        await using var f = await RecordApiFixture.Create(); var id = SeedTransferList(f, 1)[0]; var before = f.Snapshot();
        for (var i = 0; i < 120; i++) await TransferPage(f, "month=all&focusTransferId=" + D(id));
        using var limited = await f.Client.GetAsync(TransferRoot(f) + "?month=all&focusTransferId=" + D(id)); Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        using var shared = await f.Client.GetAsync($"/ledgers/{D(f.Ledger)}/people"); Assert.Equal(HttpStatusCode.TooManyRequests, shared.StatusCode); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("anonymous")] [InlineData("expired")] [InlineData("removed")]
    public async Task TransferFocusChecksAuthenticationAndMembershipBeforeDisclosingTarget(string kind)
    {
        await using var f = await RecordApiFixture.Create(); var id = SeedTransferList(f, 1)[0]; using var anonymous = f.App.Client();
        if (kind == "expired") f.App.Time.Advance(TimeSpan.FromDays(15));
        if (kind == "removed") f.App.Database.Execute("DELETE FROM ledger_members"); var before = f.Snapshot();
        if (kind == "removed")
        {
            var denied = await TransferPage(f, "month=all&focusTransferId=" + D(id), 404);
            var missing = await TransferPage(f, "month=all&focusTransferId=" + Guid.NewGuid().ToString("D"), 404);
            Assert.Equal(denied.ToJsonString(), missing.ToJsonString());
        }
        else
        {
            using var response = await (kind == "anonymous" ? anonymous : f.Client).GetAsync(TransferRoot(f) + "?month=all&focusTransferId=" + D(id));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); Assert.DoesNotContain("transfers", await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(before, f.Snapshot());
    }

}
