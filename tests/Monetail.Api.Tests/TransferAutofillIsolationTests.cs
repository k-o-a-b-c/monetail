using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Monetail.Api.Tests;

public sealed partial class ReceiptApiTests
{
    [Fact]
    public async Task TransferAutofillSeparatesSameTransferIdsAndDoesNotExposeForeignSpecifiedSources()
    {
        await using var f = await Fixture.Create(); TransferCandidate(f, 1, "振替");
        using var ledger = await f.Client.PostAsJsonAsync("/ledgers", new { requestId = Guid.NewGuid(), name = "別家計簿" }); Assert.Equal(HttpStatusCode.Created, ledger.StatusCode);
        var other = (await ledger.Content.ReadFromJsonAsync<JsonObject>())!["ledgerId"]!.GetValue<string>();
        using var person = await f.Client.PostAsJsonAsync($"/ledgers/{D(other)}/people", new { requestId = Guid.NewGuid(), name = "別人物" }); Assert.Equal(HttpStatusCode.Created, person.StatusCode);
        var owner = (await person.Content.ReadFromJsonAsync<JsonObject>())!["people"]![0]!["personId"]!.GetValue<string>(); var payments = new List<string>();
        foreach (var name in new[] { "別元", "別先" })
        {
            using var payment = await f.Client.PostAsJsonAsync($"/ledgers/{D(other)}/payment-methods", new { requestId = Guid.NewGuid(), ownerPersonId = D(owner), name }); Assert.Equal(HttpStatusCode.Created, payment.StatusCode);
            payments.Add((await payment.Content.ReadFromJsonAsync<JsonObject>())!["paymentMethodId"]!.GetValue<string>());
        }
        SeedTransferList(f, 1, payments[0], payments[1], ledger: other); var before = f.Snapshot();
        AssertTransferPair(await TransferAutofill(f), f.Payment, f.OtherPayment); AssertTransferPair(await TransferAutofill(f, ledger: other), payments[0], payments[1]);
        var foreign = await TransferAutofill(f, TransferAutofillQuery(from: D(payments[0])), 404); var missing = await TransferAutofill(f, TransferAutofillQuery(from: Guid.NewGuid().ToString("D")), 404);
        Assert.Equal("not_found", foreign["code"]!.GetValue<string>()); Assert.Equal(foreign.ToJsonString(), missing.ToJsonString()); Assert.Equal(before, f.Snapshot());
        f.App.Database.Execute($"DELETE FROM ledger_members WHERE ledger_id='{other}'"); before = f.Snapshot(); Assert.Equal("not_found", (await TransferAutofill(f, status: 404, ledger: other))["code"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }
}
