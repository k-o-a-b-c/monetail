using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Features.Monetail.Transfers;
using Monetail.Api.Persistence;
using Xunit;

namespace Monetail.Api.Tests;

public sealed partial class ReceiptApiTests
{
    [Fact]
    public async Task TransferCreateReturnsEmpty201LocationAndPreservesAllFieldsAndAudit()
    {
        await using var f = await Fixture.Create(); var input = TransferInput(f); input["name"] = "  銀行 → 現金  "; input["amount"] = int.MaxValue;
        var other = f.App.Database.Tables().Except(["ledger_transfers", "operation_requests"]); var before = f.App.Database.Snapshot(other);
        var location = await CreateTransfer(f, input); var id = location.Split('/').Last();
        var detail = await ReadTransfer(f.Client, location); AssertD(detail["transferId"]!, '7'); AssertD(detail["revision"]!, '4');
        Assert.Equal(input["name"]!.GetValue<string>(), detail["name"]!.GetValue<string>()); Assert.Equal(int.MaxValue, detail["amount"]!.GetValue<int>());
        Assert.Equal("2026-11-10T12:34:56", detail["transactionAt"]!.GetValue<string>()); Assert.Null(detail["memo"]);
        await using var services = f.App.Database.CreateServices(); await using var scope = services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var stored = await db.Set<LedgerTransfer>().SingleAsync(); Assert.Equal(Guid.Parse(id).ToString("N"), stored.Id);
        Assert.Equal(DateTimeKind.Unspecified, stored.TransactionAt.Kind); Assert.Equal(f.App.UserId, stored.CreatedByAuthUserId); Assert.Equal(stored.CreatedByAuthUserId, stored.UpdatedByAuthUserId);
        Assert.Equal(f.App.Time.GetUtcNow().UtcDateTime.Ticks, stored.CreatedAtUtc.Ticks); Assert.Equal(stored.CreatedAtUtc, stored.UpdatedAtUtc);
        Assert.Equal(1L, f.App.Database.Scalar("SELECT count(*) FROM operation_requests WHERE operation_type='transfer.create'")); Assert.Equal(before, f.App.Database.Snapshot(other));
        Assert.Single((await TransferPage(f, "month=2026-11"))["transfers"]!.AsArray());
    }

    [Fact]
    public async Task TransferCreateSameRequestReturnsSameLocationWithoutAnyChanges()
    {
        await using var f = await Fixture.Create(); var input = TransferInput(f); input["memo"] = ""; var location = await CreateTransfer(f, input);
        var before = f.Snapshot(); input["memo"] = null; Assert.Equal(location, await CreateTransfer(f, input)); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("Admin", "cross", 201)] [InlineData("Editor", "own", 201)]
    [InlineData("Editor", "cross", 403)] [InlineData("Editor", "reverse", 403)] [InlineData("Editor", "unmapped", 403)] [InlineData("Viewer", "own", 403)]
    public async Task TransferCreateEnforcesBothOwnersAndCurrentLedgerRole(string role, string mode, int status)
    {
        await using var f = await Fixture.Create(); var input = TransferInput(f);
        if (mode == "own") input["toPaymentMethodId"] = D(await OwnTransferPayment(f));
        if (mode == "reverse") { input["fromPaymentMethodId"] = D(f.OtherPayment); input["toPaymentMethodId"] = D(f.Payment); }
        f.Role(role, mode == "unmapped" ? null : f.Owner); var before = f.Snapshot();
        if (status == 201) await CreateTransfer(f, input);
        else { var error = await TransferCreateError(f, input, status); Assert.Equal("forbidden", error["code"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot()); }
    }

    [Theory]
    [InlineData("from", "payment", false)] [InlineData("to", "payment", false)] [InlineData("from", "person", false)] [InlineData("to", "person", false)]
    [InlineData("from", "payment", true)] [InlineData("to", "payment", true)] [InlineData("from", "person", true)] [InlineData("to", "person", true)]
    public async Task TransferCreateChecksBothPaymentAndPersonPeriodsWithInclusiveBoundaries(string side, string kind, bool boundary)
    {
        await using var f = await Fixture.Create(); var input = TransferInput(f); var id = kind == "payment" ? (side == "from" ? f.Payment : f.OtherPayment) : (side == "from" ? f.Owner : f.Other);
        var table = kind == "payment" ? "ledger_payment_methods" : "ledger_people"; var column = kind == "payment" ? "payment_method_id" : "person_id";
        f.App.Database.Execute($"UPDATE {table} SET valid_from='{(boundary ? "2026-11-10" : "2026-11-11")}',valid_to='2026-11-11' WHERE {column}='{id}'");
        var before = f.Snapshot();
        if (boundary) await CreateTransfer(f, input);
        else { var error = await TransferCreateError(f, input, 400); Assert.Equal("invalid_input", error["code"]!.GetValue<string>()); Assert.Equal(side + "PaymentMethodId", error["field"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot()); }
    }

    [Theory]
    [InlineData("from", false)] [InlineData("to", false)] [InlineData("from", true)] [InlineData("to", true)]
    public async Task TransferCreateIncludesEndDateAndRejectsFollowingDay(string side, bool person)
    {
        await using var f = await Fixture.Create(); var input = TransferInput(f); var id = person ? (side == "from" ? f.Owner : f.Other) : (side == "from" ? f.Payment : f.OtherPayment);
        var table = person ? "ledger_people" : "ledger_payment_methods"; var column = person ? "person_id" : "payment_method_id";
        f.App.Database.Execute($"UPDATE {table} SET valid_to='2026-11-10' WHERE {column}='{id}'"); await CreateTransfer(f, input);
        input["requestId"] = Guid.NewGuid().ToString("D"); input["transactionAt"] = "2026-11-11T00:00:00"; var before = f.Snapshot();
        var error = await TransferCreateError(f, input, 400); Assert.Equal(side + "PaymentMethodId", error["field"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("name", 100, false, true)] [InlineData("name", 101, false, false)] [InlineData("name", 50, true, true)] [InlineData("name", 51, true, false)]
    [InlineData("memo", 100, false, true)] [InlineData("memo", 101, false, false)] [InlineData("memo", 50, true, true)] [InlineData("memo", 51, true, false)]
    public async Task TransferCreateCountsUtf16CodeUnitsAndPreservesText(string field, int count, bool emoji, bool valid)
    {
        await using var f = await Fixture.Create(); var input = TransferInput(f); var value = emoji ? string.Concat(Enumerable.Repeat("😀", count)) : new string('あ', count); input[field] = value; var before = f.Snapshot();
        if (valid) { var detail = await ReadTransfer(f.Client, await CreateTransfer(f, input)); Assert.Equal(value, detail[field]!.GetValue<string>()); }
        else { var error = await TransferCreateError(f, input, 400); Assert.Equal(field, error["field"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot()); }
    }

    [Theory]
    [InlineData("")] [InlineData(" ")] [InlineData("\u3000\t\u00a0")]
    public async Task TransferCreateRejectsEmptyOrUnicodeWhitespaceOnlyNames(string name)
    {
        await using var f = await Fixture.Create(); var input = TransferInput(f); input["name"] = name; var before = f.Snapshot();
        Assert.Equal("name", (await TransferCreateError(f, input, 400))["field"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("a\rb")] [InlineData("a\nb")] [InlineData("a\r\nb")] [InlineData("a\vb")] [InlineData("a\fb")] [InlineData("a\u0085b")] [InlineData("a\u2028b")] [InlineData("a\u2029b")]
    public async Task TransferCreateRejectsAllMemoLineBreaksWithoutStrippingThem(string memo)
    {
        await using var f = await Fixture.Create(); var input = TransferInput(f); input["memo"] = memo; var before = f.Snapshot();
        Assert.Equal("memo", (await TransferCreateError(f, input, 400))["field"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferCreateKeepsWhitespaceMemoAndNormalizesUuidCaseForReplay()
    {
        await using var f = await Fixture.Create(); var input = TransferInput(f); input["memo"] = " "; var location = await CreateTransfer(f, input);
        Assert.Equal(" ", (await ReadTransfer(f.Client, location))["memo"]!.GetValue<string>()); var before = f.Snapshot();
        foreach (var field in new[] { "requestId", "fromPaymentMethodId", "toPaymentMethodId" }) input[field] = input[field]!.GetValue<string>().ToUpperInvariant();
        Assert.Equal(location, await CreateTransfer(f, input)); Assert.Equal(before, f.Snapshot());
    }

    private static async Task<string> OwnTransferPayment(Fixture f)
    {
        using var response = await f.Client.PostAsJsonAsync($"/ledgers/{D(f.Ledger)}/payment-methods", new { requestId = Guid.NewGuid(), ownerPersonId = D(f.Owner), name = "本人の預金" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); return (await response.Content.ReadFromJsonAsync<JsonObject>())!["paymentMethodId"]!.GetValue<string>();
    }

    private static JsonObject TransferInput(Fixture f) => new()
    {
        ["requestId"] = Guid.NewGuid().ToString("D"), ["transactionAt"] = "2026-11-10T12:34:56", ["name"] = "振替",
        ["fromPaymentMethodId"] = D(f.Payment), ["toPaymentMethodId"] = D(f.OtherPayment), ["amount"] = 1, ["memo"] = null
    };
    private static async Task<string> CreateTransfer(Fixture f, JsonObject input, HttpClient? client = null)
    {
        using var response = await (client ?? f.Client).PostAsJsonAsync(TransferRoot(f), input); var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, body); Assert.Empty(body);
        Assert.NotNull(response.Headers.Location); var location = response.Headers.Location.OriginalString;
        Assert.StartsWith($"/ledgers/{D(f.Ledger)}/transfers/", location); Assert.True(Guid.TryParseExact(location.Split('/').Last(), "D", out _)); return location;
    }
    private static async Task<JsonObject> TransferCreateError(Fixture f, JsonObject input, int status, HttpClient? client = null)
    {
        using var response = await (client ?? f.Client).PostAsJsonAsync(TransferRoot(f), input); var body = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == status, body); Assert.Null(response.Headers.Location); return JsonNode.Parse(body)!.AsObject();
    }
}
