using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Monetail.Api.Tests;

public sealed partial class ReceiptApiTests
{
    [Theory]
    [InlineData("Admin", "cross", 204)] [InlineData("Editor", "own", 204)] [InlineData("Viewer", "own", 403)]
    [InlineData("Editor", "unmapped", 403)] [InlineData("Editor", "old-from", 403)] [InlineData("Editor", "old-to", 403)]
    [InlineData("Editor", "new-from", 403)] [InlineData("Editor", "new-to", 403)]
    public async Task TransferUpdateChecksAllFourOwnersAndHouseholdRole(string role, string mode, int status)
    {
        await using var f = await Fixture.Create(); var own = await OwnTransferPayment(f); var create = TransferInput(f); create["toPaymentMethodId"] = D(own);
        if (mode == "old-from") create["fromPaymentMethodId"] = D(f.OtherPayment);
        if (mode == "old-to" || mode == "cross") create["toPaymentMethodId"] = D(f.OtherPayment);
        var path = await CreateTransfer(f, create); var input = await TransferUpdateInput(f, path); input["amount"] = 2;
        if (mode == "old-from") input["fromPaymentMethodId"] = D(f.Payment);
        if (mode == "old-to") input["toPaymentMethodId"] = D(own);
        if (mode == "new-from") input["fromPaymentMethodId"] = D(f.OtherPayment);
        if (mode == "new-to") input["toPaymentMethodId"] = D(f.OtherPayment);
        f.Role(role, mode == "unmapped" ? null : f.Owner); var before = f.Snapshot();
        if (status == 204) await UpdateTransfer(f, path, input);
        else { var error = await TransferUpdateError(f, path, input, status); Assert.Equal("forbidden", error["code"]!.GetValue<string>()); Assert.Null(error["latest"]); Assert.Equal(before, f.Snapshot()); }
    }

    [Theory]
    [InlineData("from", false, false)] [InlineData("to", false, false)] [InlineData("from", true, false)] [InlineData("to", true, false)]
    [InlineData("from", false, true)] [InlineData("to", false, true)] [InlineData("from", true, true)] [InlineData("to", true, true)]
    public async Task TransferUpdateValidatesBothMastersAndInclusivePeriodBoundaries(string side, bool person, bool boundary)
    {
        await using var f = await Fixture.Create(); var path = await CreateTransfer(f, TransferInput(f)); var input = await TransferUpdateInput(f, path); input["amount"] = 2;
        var id = person ? (side == "from" ? f.Owner : f.Other) : (side == "from" ? f.Payment : f.OtherPayment);
        var table = person ? "ledger_people" : "ledger_payment_methods"; var column = person ? "person_id" : "payment_method_id";
        f.App.Database.Execute($"UPDATE {table} SET valid_from='{(boundary ? "2026-11-10" : "2026-11-11")}',valid_to='2026-11-11' WHERE {column}='{id}'"); var before = f.Snapshot();
        if (!boundary) { Assert.Equal(side + "PaymentMethodId", (await TransferUpdateError(f, path, input, 400))["field"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot()); return; }
        await UpdateTransfer(f, path, input); input = await TransferUpdateInput(f, path); input["transactionAt"] = "2026-11-11T23:59:59"; await UpdateTransfer(f, path, input);
        input = await TransferUpdateInput(f, path); input["transactionAt"] = "2026-11-12T00:00:00"; before = f.Snapshot();
        Assert.Equal(side + "PaymentMethodId", (await TransferUpdateError(f, path, input, 400))["field"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("missing")] [InlineData("ledger")] [InlineData("member")]
    public async Task TransferUpdateDoesNotExposeMissingOrOtherHouseholdTargets(string mode)
    {
        await using var f = await Fixture.Create(); var path = await CreateTransfer(f, TransferInput(f)); var input = await TransferUpdateInput(f, path);
        if (mode == "missing") path = TransferRoot(f) + "/" + Guid.NewGuid();
        if (mode == "ledger")
        {
            using var response = await f.Client.PostAsJsonAsync("/ledgers", new { requestId = Guid.NewGuid(), name = "別家計簿" }); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var other = (await response.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>())!["ledgerId"]!.GetValue<string>(); path = $"/ledgers/{D(other)}/transfers/{path.Split('/').Last()}";
        }
        if (mode == "member") f.App.Database.Execute("DELETE FROM ledger_members");
        var before = f.Snapshot(); var error = await TransferUpdateError(f, path, input, 404); Assert.Equal("not_found", error["code"]!.GetValue<string>()); Assert.Null(error["latest"]); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("from")] [InlineData("to")]
    public async Task TransferUpdateRejectsUnknownAndOtherHouseholdPayment(string side)
    {
        await using var f = await Fixture.Create(); var path = await CreateTransfer(f, TransferInput(f)); var input = await TransferUpdateInput(f, path);
        using var ledgerResponse = await f.Client.PostAsJsonAsync("/ledgers", new { requestId = Guid.NewGuid(), name = "別家計簿" }); Assert.Equal(HttpStatusCode.Created, ledgerResponse.StatusCode);
        var otherLedger = (await ledgerResponse.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>())!["ledgerId"]!.GetValue<string>();
        using var personResponse = await f.Client.PostAsJsonAsync($"/ledgers/{D(otherLedger)}/people", new { requestId = Guid.NewGuid(), name = "別人物" }); Assert.Equal(HttpStatusCode.Created, personResponse.StatusCode);
        var otherPerson = (await personResponse.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>())!["people"]![0]!["personId"]!.GetValue<string>();
        using var paymentResponse = await f.Client.PostAsJsonAsync($"/ledgers/{D(otherLedger)}/payment-methods", new { requestId = Guid.NewGuid(), ownerPersonId = D(otherPerson), name = "別口座" }); Assert.Equal(HttpStatusCode.Created, paymentResponse.StatusCode);
        var otherPayment = (await paymentResponse.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>())!["paymentMethodId"]!.GetValue<string>();
        input[side + "PaymentMethodId"] = D(otherPayment); var before = f.Snapshot(); Assert.Equal("not_found", (await TransferUpdateError(f, path, input, 404))["code"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
        input[side + "PaymentMethodId"] = Guid.NewGuid().ToString("D"); await TransferUpdateError(f, path, input, 404); Assert.Equal(before, f.Snapshot());
    }
}
