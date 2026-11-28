using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;
using static Monetail.Api.Tests.RecordApiTestData;

namespace Monetail.Api.Tests;

public sealed partial class MasterPeriodApiTests
{
    [Theory]
    [InlineData("from", "validFrom", false)] [InlineData("to", "validFrom", false)]
    [InlineData("from", "validTo", false)] [InlineData("to", "validTo", false)]
    [InlineData("from", "validFrom", true)] [InlineData("to", "validFrom", true)]
    [InlineData("from", "validTo", true)] [InlineData("to", "validTo", true)]
    public async Task PaymentMethodPeriodChangeChecksExistingTransfersOnBothSides(string side, string field, bool boundary)
    {
        await using var f = await RecordApiFixture.Create();
        await CreateTransfer(f, TransferInput(f));
        var payment = side == "from" ? f.Payment : f.OtherPayment;
        var revision = (string)f.App.Database.Scalar($"SELECT revision FROM ledger_payment_methods WHERE payment_method_id='{payment}'")!;
        var date = boundary ? "2026-11-10" : field == "validFrom" ? "2026-11-11" : "2026-11-09";
        var input = new JsonObject { ["revision"] = revision, [field] = date };
        var before = f.Snapshot();
        using var response = await f.Client.PatchAsJsonAsync($"/ledgers/{D(f.Ledger)}/payment-methods/{D(payment)}", input);
        var body = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        if (boundary)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var updated = body["paymentMethods"]!.AsArray().Single(row => row!["paymentMethodId"]!.GetValue<string>() == payment)!.AsObject();
            Assert.Equal(date, updated[field]!.GetValue<string>());
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("invalid_input", body["code"]!.GetValue<string>());
            Assert.Equal(field, body["field"]!.GetValue<string>());
            Assert.Equal(before, f.Snapshot());
        }
    }

    [Fact]
    public async Task PaymentMethodPeriodChangeDoesNotConsiderUnrelatedTransfers()
    {
        await using var f = await RecordApiFixture.Create();
        await CreateTransfer(f, TransferInput(f));
        var payment = await OwnTransferPayment(f);
        var revision = (string)f.App.Database.Scalar($"SELECT revision FROM ledger_payment_methods WHERE payment_method_id='{Guid.Parse(payment):N}'")!;
        using var response = await f.Client.PatchAsJsonAsync($"/ledgers/{D(f.Ledger)}/payment-methods/{payment}",
            new { revision, validFrom = "2026-11-11" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        var updated = body["paymentMethods"]!.AsArray().Single(row => row!["paymentMethodId"]!.GetValue<string>() == Guid.Parse(payment).ToString("N"))!.AsObject();
        Assert.Equal("2026-11-11", updated["validFrom"]!.GetValue<string>());
    }
}
