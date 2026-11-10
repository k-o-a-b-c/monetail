using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Monetail.Api.Tests;

public sealed partial class ReceiptApiTests
{
    [Theory]
    [InlineData("Admin", "owner")] [InlineData("Editor", "owner")] [InlineData("Viewer", "owner")]
    [InlineData("Admin", "other")] [InlineData("Editor", "other")] [InlineData("Viewer", "other")]
    [InlineData("Admin", "none")] [InlineData("Editor", "none")] [InlineData("Viewer", "none")]
    public async Task HistoryAutofillRestrictsPaymentToCurrentPersonAndSaveRole(string role, string mapping)
    {
        await using var f = await Fixture.Create(); f.Role(role, mapping == "owner" ? f.Owner : mapping == "other" ? f.Other : null);
        HistoryReceipt(f, 1, "店舗"); HistoryReceipt(f, 2, "店舗", f.OtherPayment, inclusion: 1, rounding: "half_up");
        var result = await HistoryGet(f, "autofill", AutofillQuery());
        Assert.Equal(role == "Viewer" || mapping == "none" ? null : D(mapping == "owner" ? f.Payment : f.OtherPayment), result["paymentMethodId"]?.GetValue<string>());
        Assert.Equal(1, result["taxInclusionMode"]!.GetValue<int>()); Assert.Equal("half_up", result["taxRoundingMode"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("payment", "2026-03-31", false)] [InlineData("payment", "2026-04-01", true)] [InlineData("payment", "2026-09-30", true)] [InlineData("payment", "2026-10-01", false)]
    [InlineData("person", "2026-03-31", false)] [InlineData("person", "2026-04-01", true)] [InlineData("person", "2026-09-30", true)] [InlineData("person", "2026-10-01", false)]
    public async Task HistoryAutofillChecksBothInclusiveOwnerAndPaymentPeriods(string target, string date, bool valid)
    {
        await using var f = await Fixture.Create(); f.Role("Editor", f.Owner); HistoryReceipt(f, 1, "店舗");
        var table = target == "person" ? "ledger_people" : "ledger_payment_methods";
        f.App.Database.Execute($"UPDATE {table} SET valid_from='2026-04-01',valid_to='2026-09-30'; UPDATE ledger_receipts SET transaction_at='2026-06-01T00:00:00'");
        var result = await HistoryGet(f, "autofill", AutofillQuery(date));
        Assert.Equal(valid ? D(f.Payment) : null, result["paymentMethodId"]?.GetValue<string>()); Assert.Equal(0, result["taxInclusionMode"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("abc")] [InlineData("ＡＢＣ")] [InlineData("ABC支店")] [InlineData(" ABC")]
    public async Task HistoryAutofillHasNoNormalizedOrPartialFallback(string shop)
    {
        await using var f = await Fixture.Create(); f.Role("Editor", f.Owner); HistoryReceipt(f, 1, "ABC");
        var result = await HistoryGet(f, "autofill", "shopName=" + Uri.EscapeDataString(shop) + "&transactionDate=2026-11-10");
        Keys(result, "paymentMethodId", "taxInclusionMode", "taxRoundingMode"); Assert.All(result, pair => Assert.Null(pair.Value));
    }

    [Fact]
    public async Task HistoryAutofillUsesUpdatedUtcThenBinaryIdAndSkipsUnusableNewerPayment()
    {
        await using var f = await Fixture.Create(); f.Role("Editor", f.Owner);
        using var created = await f.Client.PostAsJsonAsync($"/ledgers/{f.Ledger}/payment-methods", new { requestId = Guid.NewGuid(), ownerPersonId = f.Owner, name = "もう一つ" });
        var second = (await created.Content.ReadFromJsonAsync<JsonObject>())!["paymentMethodId"]!.GetValue<string>();
        HistoryReceipt(f, 1, "店舗", second); HistoryReceipt(f, 2, "店舗", inclusion: 1, rounding: "half_up");
        var initial = await HistoryGet(f, "autofill", AutofillQuery()); Assert.Equal(D(f.Payment), initial["paymentMethodId"]!.GetValue<string>()); Assert.Equal(1, initial["taxInclusionMode"]!.GetValue<int>());
        f.App.Database.Execute($"UPDATE ledger_payment_methods SET valid_to='2025-01-01' WHERE payment_method_id='{f.Payment}'; UPDATE ledger_receipts SET transaction_at='2024-01-01T00:00:00' WHERE payment_method_id='{f.Payment}'");
        var older = await HistoryGet(f, "autofill", AutofillQuery()); Assert.Equal(D(second), older["paymentMethodId"]!.GetValue<string>()); Assert.Equal(1, older["taxInclusionMode"]!.GetValue<int>());
        f.App.Database.Execute("UPDATE ledger_receipts SET updated_at_utc='2026-11-11 00:00:00',transaction_at='2020-01-01T00:00:00' WHERE receipt_id='00000000000000000000000000000001'");
        Assert.Equal(0, (await HistoryGet(f, "autofill", AutofillQuery()))["taxInclusionMode"]!.GetValue<int>());
    }

    private static string AutofillQuery(string date = "2026-11-10") => "shopName=" + Uri.EscapeDataString("店舗") + "&transactionDate=" + date;
}
