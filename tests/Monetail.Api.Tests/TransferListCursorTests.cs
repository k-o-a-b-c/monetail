using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Monetail.Api.Tests.RecordApiTestData;

namespace Monetail.Api.Tests;

public sealed partial class TransferListApiTests
{
    [Theory]
    [InlineData("month")] [InlineData("owner")] [InlineData("payment")] [InlineData("q")] [InlineData("q-case")]
    public async Task TransferListCursorCannotBeReusedWithDifferentSearchConditions(string change)
    {
        await using var f = await RecordApiFixture.Create(); SeedTransferList(f, 101, name: "スーパーABC");
        var initial = "month=all&ownerPersonId=" + D(f.Owner) + "&paymentMethodId=" + D(f.Payment) + "&q=ABC";
        var cursor = (await TransferPage(f, initial))["nextCursor"]!.GetValue<string>();
        var next = change switch
        {
            "month" => initial.Replace("month=all", "month=2026-11"),
            "owner" => initial.Replace(D(f.Owner), D(f.Other)),
            "payment" => initial.Replace(D(f.Payment), D(f.OtherPayment)),
            "q-case" => initial.Replace("q=ABC", "q=abc"),
            _ => initial.Replace("q=ABC", "q=other")
        };
        var error = await TransferPage(f, next + "&cursor=" + Uri.EscapeDataString(cursor), 400);
        Assert.Equal("invalid_cursor", error["code"]!.GetValue<string>()); Assert.Null(error["latest"]);
    }

    [Theory]
    [InlineData("")] [InlineData("invalid")] [InlineData("%00")] [InlineData("eyJpZCI6MX0")]
    public async Task TransferListRejectsMalformedCursorWithoutLeakingData(string cursor)
    {
        await using var f = await RecordApiFixture.Create(); SeedTransferList(f, 1);
        var error = await TransferPage(f, "month=all&cursor=" + cursor, 400); Assert.Equal("invalid_cursor", error["code"]!.GetValue<string>()); Assert.False(error.ContainsKey("transfers"));
    }

    [Theory]
    [InlineData("tamper")] [InlineData("date")] [InlineData("id")] [InlineData("json")] [InlineData("purpose")] [InlineData("keys")]
    public async Task TransferListRejectsTamperingAndUnverifiableProtectedPayloads(string change)
    {
        await using var f = await RecordApiFixture.Create(); SeedTransferList(f, 101);
        var cursor = (await TransferPage(f, "month=all"))["nextCursor"]!.GetValue<string>();
        var provider = f.App.Factory.Services.GetRequiredService<IDataProtectionProvider>(); var protector = provider.CreateProtector("Monetail.TransferList.Cursor.v1");
        if (change == "tamper") cursor = cursor[..(cursor.Length / 2)] + (cursor[cursor.Length / 2] == 'A' ? 'B' : 'A') + cursor[(cursor.Length / 2 + 1)..];
        else if (change == "json") cursor = protector.Protect("{broken");
        else if (change == "purpose") cursor = provider.CreateProtector("other-purpose").Protect("{}");
        else if (change == "keys") cursor = new EphemeralDataProtectionProvider().CreateProtector("Monetail.TransferList.Cursor.v1").Protect("{}");
        else
        {
            var payload = JsonNode.Parse(protector.Unprotect(cursor))!.AsObject(); payload[change == "date" ? "TransactionAt" : "TransferId"] = "bad";
            cursor = protector.Protect(payload.ToJsonString());
        }
        Assert.Equal("invalid_cursor", (await TransferPage(f, "month=all&cursor=" + Uri.EscapeDataString(cursor), 400))["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task TransferListCursorSurvivesHostRestartAndBoundaryDeletionWithoutOffset()
    {
        await using var f = await RecordApiFixture.Create(); var ids = SeedTransferList(f, 101); var page = await TransferPage(f, "month=all");
        var cursor = page["nextCursor"]!.GetValue<string>();
        f.App.Database.Execute($"DELETE FROM ledger_transfers WHERE transfer_id='{ids[1]}'"); // 前ページの末尾が消えても、位置自体で続ける。
        using var host = f.App.NewFactory(); using var client = f.App.Client(host); await f.App.LoginAsync(client);
        var next = await TransferPage(f, "month=all&cursor=" + Uri.EscapeDataString(cursor), client: client);
        Assert.Single(next["transfers"]!.AsArray()); Assert.Equal(D(ids[0]), next["transfers"]![0]!["transferId"]!.GetValue<string>()); Assert.Null(next["nextCursor"]);
    }

    [Fact]
    public async Task TransferListCursorIsBoundToLedgerAndDoesNotExposeAnotherLedger()
    {
        await using var f = await RecordApiFixture.Create(); SeedTransferList(f, 101);
        var cursor = (await TransferPage(f, "month=all"))["nextCursor"]!.GetValue<string>();
        using var create = await f.Client.PostAsJsonAsync("/ledgers", new { requestId = Guid.NewGuid(), name = "別家計簿" });
        var other = (await create.Content.ReadFromJsonAsync<JsonObject>())!["ledgerId"]!.GetValue<string>();
        using var response = await f.Client.GetAsync($"/ledgers/{D(other)}/transfers?month=all&cursor={Uri.EscapeDataString(cursor)}");
        Assert.Equal(400, (int)response.StatusCode); Assert.Equal("invalid_cursor", (await response.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>());
        using var plain = await f.Client.GetAsync($"/ledgers/{D(other)}/transfers?month=all");
        Assert.Empty((await plain.Content.ReadFromJsonAsync<JsonObject>())!["transfers"]!.AsArray());
    }
    [Fact]
    public async Task TransferListCursorDistinguishesOmittedAndEmptySearchAndReceiptPurpose()
    {
        await using var f = await RecordApiFixture.Create(); SeedTransferList(f, 101); SeedList(f, 101);
        var transfer = (await TransferPage(f, "month=all"))["nextCursor"]!.GetValue<string>();
        var receipt = (await ListPage(f, "month=all"))["nextCursor"]!.GetValue<string>(); var before = f.Snapshot();
        Assert.Equal("invalid_cursor", (await TransferPage(f, "month=all&q=&cursor=" + Uri.EscapeDataString(transfer), 400))["code"]!.GetValue<string>());
        Assert.Equal("invalid_cursor", (await TransferPage(f, "month=all&cursor=" + Uri.EscapeDataString(receipt), 400))["code"]!.GetValue<string>());
        Assert.Equal("invalid_cursor", (await ListPage(f, "month=all&cursor=" + Uri.EscapeDataString(transfer), 400))["code"]!.GetValue<string>());
        var protector = f.App.Factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("Monetail.TransferList.Cursor.v1");
        var payload = JsonNode.Parse(protector.Unprotect(transfer))!.AsObject(); Assert.Equal(f.Ledger, payload["LedgerId"]!.GetValue<string>());
        Assert.Equal(64, payload["Conditions"]!.GetValue<string>().Length); Assert.Equal(before, f.Snapshot());
    }

}
