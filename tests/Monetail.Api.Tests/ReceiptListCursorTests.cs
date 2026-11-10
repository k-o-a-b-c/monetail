using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Monetail.Api.Tests;

public sealed partial class ReceiptApiTests
{
    [Theory]
    [InlineData("month")] [InlineData("owner")] [InlineData("payment")] [InlineData("q")] [InlineData("q-case")]
    public async Task ListCursorCannotBeReusedWithDifferentSearchConditions(string change)
    {
        await using var f = await Fixture.Create(); SeedList(f, 101, shop: "スーパーABC");
        var initial = "month=all&ownerPersonId=" + D(f.Owner) + "&paymentMethodId=" + D(f.Payment) + "&q=ABC";
        var cursor = (await ListPage(f, initial))["nextCursor"]!.GetValue<string>();
        var next = change switch
        {
            "month" => initial.Replace("month=all", "month=2026-11"),
            "owner" => initial.Replace(D(f.Owner), D(f.Other)),
            "payment" => initial.Replace(D(f.Payment), D(f.OtherPayment)),
            "q-case" => initial.Replace("q=ABC", "q=abc"),
            _ => initial.Replace("q=ABC", "q=other")
        };
        var error = await ListPage(f, next + "&cursor=" + Uri.EscapeDataString(cursor), 400);
        Assert.Equal("invalid_cursor", error["code"]!.GetValue<string>()); Assert.Null(error["latest"]);
    }

    [Theory]
    [InlineData("")] [InlineData("invalid")] [InlineData("%00")] [InlineData("eyJpZCI6MX0")]
    public async Task ListRejectsMalformedCursorWithoutLeakingData(string cursor)
    {
        await using var f = await Fixture.Create(); SeedList(f, 1);
        var error = await ListPage(f, "month=all&cursor=" + cursor, 400); Assert.Equal("invalid_cursor", error["code"]!.GetValue<string>()); Assert.False(error.ContainsKey("receipts"));
    }

    [Theory]
    [InlineData("tamper")] [InlineData("date")] [InlineData("id")] [InlineData("json")] [InlineData("purpose")] [InlineData("keys")]
    public async Task ListRejectsTamperingAndUnverifiableProtectedPayloads(string change)
    {
        await using var f = await Fixture.Create(); SeedList(f, 101);
        var cursor = (await ListPage(f, "month=all"))["nextCursor"]!.GetValue<string>();
        var provider = f.App.Factory.Services.GetRequiredService<IDataProtectionProvider>(); var protector = provider.CreateProtector("Monetail.ReceiptList.Cursor.v1");
        if (change == "tamper") cursor = cursor[..(cursor.Length / 2)] + (cursor[cursor.Length / 2] == 'A' ? 'B' : 'A') + cursor[(cursor.Length / 2 + 1)..];
        else if (change == "json") cursor = protector.Protect("{broken");
        else if (change == "purpose") cursor = provider.CreateProtector("other-purpose").Protect("{}");
        else if (change == "keys") cursor = new EphemeralDataProtectionProvider().CreateProtector("Monetail.ReceiptList.Cursor.v1").Protect("{}");
        else
        {
            var payload = JsonNode.Parse(protector.Unprotect(cursor))!.AsObject(); payload[change == "date" ? "TransactionAt" : "ReceiptId"] = "bad";
            cursor = protector.Protect(payload.ToJsonString());
        }
        Assert.Equal("invalid_cursor", (await ListPage(f, "month=all&cursor=" + Uri.EscapeDataString(cursor), 400))["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task ListCursorSurvivesHostRestartAndBoundaryDeletionWithoutOffset()
    {
        await using var f = await Fixture.Create(); var ids = SeedList(f, 101); var page = await ListPage(f, "month=all");
        var cursor = page["nextCursor"]!.GetValue<string>();
        f.App.Database.Execute($"DELETE FROM ledger_receipts WHERE receipt_id='{ids[1]}'"); // 前ページの末尾が消えても、位置自体で続ける。
        using var host = f.App.NewFactory(); using var client = f.App.Client(host); await f.App.LoginAsync(client);
        var next = await ListPage(f, "month=all&cursor=" + Uri.EscapeDataString(cursor), client: client);
        Assert.Single(next["receipts"]!.AsArray()); Assert.Equal(D(ids[0]), next["receipts"]![0]!["receiptId"]!.GetValue<string>()); Assert.Null(next["nextCursor"]);
    }

    [Fact]
    public async Task ListCursorIsBoundToLedgerAndDoesNotExposeAnotherLedger()
    {
        await using var f = await Fixture.Create(); SeedList(f, 101);
        var cursor = (await ListPage(f, "month=all"))["nextCursor"]!.GetValue<string>();
        using var create = await f.Client.PostAsJsonAsync("/ledgers", new { requestId = Guid.NewGuid(), name = "別家計簿" });
        var other = (await create.Content.ReadFromJsonAsync<JsonObject>())!["ledgerId"]!.GetValue<string>();
        using var response = await f.Client.GetAsync($"/ledgers/{D(other)}/receipts?month=all&cursor={Uri.EscapeDataString(cursor)}");
        Assert.Equal(400, (int)response.StatusCode); Assert.Equal("invalid_cursor", (await response.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>());
        using var plain = await f.Client.GetAsync($"/ledgers/{D(other)}/receipts?month=all");
        Assert.Empty((await plain.Content.ReadFromJsonAsync<JsonObject>())!["receipts"]!.AsArray());
    }
}
