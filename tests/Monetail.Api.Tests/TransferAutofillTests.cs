using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;
using static Monetail.Api.Tests.RecordApiTestData;

namespace Monetail.Api.Tests;

public sealed partial class TransferAutofillApiTests
{
    [Fact]
    public async Task TransferAutofillEmptyHistoryReturnsExactlyTwoNullsWithoutWriting()
    {
        await using var f = await RecordApiFixture.Create(); var before = f.Snapshot(); var result = await TransferAutofill(f);
        AssertTransferPair(result, null, null); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferAutofillAdminReturnsBothSidesOfSameLatestUsableHistory()
    {
        await using var f = await RecordApiFixture.Create(); TransferCandidate(f, 1, "振替"); var before = f.Snapshot();
        AssertTransferPair(await TransferAutofill(f), f.Payment, f.OtherPayment); Assert.Equal(before, f.Snapshot());
    }

    private static async Task<JsonObject> TransferAutofill(RecordApiFixture f, string? query = null, int status = 200, HttpClient? client = null, string? ledger = null)
    {
        using var response = await (client ?? f.Client).GetAsync($"/ledgers/{D(ledger ?? f.Ledger)}/transfer-autofill?" + (query ?? TransferAutofillQuery()));
        var text = await response.Content.ReadAsStringAsync(); Assert.True((int)response.StatusCode == status, $"Expected {status}, got {(int)response.StatusCode}: {text}"); return JsonNode.Parse(text)!.AsObject();
    }
    private static string TransferAutofillQuery(string name = "振替", string date = "2026-11-10", string? from = null) =>
        "name=" + Uri.EscapeDataString(name) + "&transactionDate=" + Uri.EscapeDataString(date) + (from is null ? "" : "&fromPaymentMethodId=" + Uri.EscapeDataString(from));
    private static void AssertTransferPair(JsonObject result, string? from, string? to)
    {
        Keys(result, "fromPaymentMethodId", "toPaymentMethodId");
        Assert.Equal(from is null ? null : D(from), result["fromPaymentMethodId"]?.GetValue<string>()); Assert.Equal(to is null ? null : D(to), result["toPaymentMethodId"]?.GetValue<string>());
    }
}
