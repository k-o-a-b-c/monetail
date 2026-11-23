using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Monetail.Api.Tests;

public sealed partial class ReceiptApiTests
{
    [Theory]
    [InlineData("from", "validFrom", false)] [InlineData("to", "validFrom", false)]
    [InlineData("from", "validTo", false)] [InlineData("to", "validTo", false)]
    [InlineData("from", "validFrom", true)] [InlineData("to", "validFrom", true)]
    [InlineData("from", "validTo", true)] [InlineData("to", "validTo", true)]
    public async Task PersonPeriodChangeChecksExistingTransfersOnBothSides(string side, string field, bool boundary)
    {
        await using var f = await Fixture.Create();
        await CreateTransfer(f, TransferInput(f));
        var person = side == "from" ? f.Owner : f.Other;
        var revision = (string)f.App.Database.Scalar($"SELECT revision FROM ledger_people WHERE person_id='{person}'")!;
        var date = boundary ? "2026-11-10" : field == "validFrom" ? "2026-11-11" : "2026-11-09";
        var input = new JsonObject { ["revision"] = revision, [field] = date };
        var before = f.Snapshot();
        using var response = await f.Client.PatchAsJsonAsync($"/ledgers/{D(f.Ledger)}/people/{D(person)}", input);
        var body = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        if (boundary)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var updated = body["people"]!.AsArray().Single(row => row!["personId"]!.GetValue<string>() == person)!.AsObject();
            Assert.Equal(date, updated[field]!.GetValue<string>());
        }
        else
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("person_period_conflict", body["code"]!.GetValue<string>());
            Assert.Equal(field, body["field"]!.GetValue<string>());
            Assert.Null(body["latest"]);
            Assert.Equal(before, f.Snapshot());
        }
    }
}
