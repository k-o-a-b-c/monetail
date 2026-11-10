using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Monetail.Api.Tests;

public sealed partial class ReceiptApiTests
{
    [Fact]
    public async Task HistoryKeepsAllJoinsAndAutofillIsolatedEvenWithSameReceiptIds()
    {
        await using var f = await Fixture.Create(); f.Role("Admin", f.Owner); var id = HistoryReceipt(f, 1, "店舗", location: "公開所在地"); HistoryDetail(f, id, f.Category, "公開品名");
        using var ledger = await f.Client.PostAsJsonAsync("/ledgers", new { requestId = Guid.NewGuid(), name = "別家計簿" });
        var otherLedger = (await ledger.Content.ReadFromJsonAsync<JsonObject>())!["ledgerId"]!.GetValue<string>();
        using var person = await f.Client.PostAsJsonAsync($"/ledgers/{otherLedger}/people", new { requestId = Guid.NewGuid(), name = "別人物" });
        var otherPerson = (await person.Content.ReadFromJsonAsync<JsonObject>())!["people"]![0]!["personId"]!.GetValue<string>();
        using var payment = await f.Client.PostAsJsonAsync($"/ledgers/{otherLedger}/payment-methods", new { requestId = Guid.NewGuid(), ownerPersonId = otherPerson, name = "別支払方法" });
        var otherPayment = (await payment.Content.ReadFromJsonAsync<JsonObject>())!["paymentMethodId"]!.GetValue<string>();
        using var category = await f.Client.PostAsJsonAsync($"/ledgers/{otherLedger}/categories", new { requestId = Guid.NewGuid(), name = "別科目", incomeExpense = "Expense", taxCategoryCode = "Standard" });
        var otherCategory = (await category.Content.ReadFromJsonAsync<JsonObject>())!["categoryId"]!.GetValue<string>();
        var original = f.Ledger; f.Ledger = otherLedger;
        var otherId = HistoryReceipt(f, 1, "店舗", otherPayment, "非公開所在地", "2026-11-12 00:00:00", 1, "half_up"); HistoryDetail(f, otherId, otherCategory, "非公開品名");
        HistoryReceipt(f, 2, "非公開店名", otherPayment); f.Ledger = original;
        Assert.Equal(new[] { "店舗" }, Names(await HistoryGet(f, "suggestions", "kind=shopName")));
        Assert.Equal(new[] { "公開所在地" }, Names(await HistoryGet(f, "suggestions", HistoryScope(f, "shopLocation"))));
        Assert.Equal(new[] { "公開品名" }, Names(await HistoryGet(f, "suggestions", HistoryScope(f, "itemName"))));
        Assert.Empty(Names(await HistoryGet(f, "suggestions", "kind=itemName&shopName=" + Uri.EscapeDataString("店舗") + "&categoryId=" + D(otherCategory))));
        var fill = await HistoryGet(f, "autofill", AutofillQuery()); Assert.Equal(0, fill["taxInclusionMode"]!.GetValue<int>()); Assert.Equal(D(f.Payment), fill["paymentMethodId"]!.GetValue<string>());
    }

    [Fact]
    public async Task HistoryParallelSearchesAndAutofillDoNotMixQueryState()
    {
        await using var f = await Fixture.Create(); f.Role("Editor", f.Owner);
        for (var i = 1; i <= 8; i++) { var id = HistoryReceipt(f, i, "店舗" + i, location: "所在地" + i); HistoryDetail(f, id, f.Category, "品名" + i); }
        var before = f.Snapshot();
        await Task.WhenAll(Enumerable.Range(1, 8).Select(async i =>
        {
            var shop = Uri.EscapeDataString("店舗" + i);
            Assert.Equal(new[] { "店舗" + i }, Names(await HistoryGet(f, "suggestions", "kind=shopName&q=" + shop)));
            Assert.Equal(new[] { "所在地" + i }, Names(await HistoryGet(f, "suggestions", "kind=shopLocation&shopName=" + shop + "&q=" + i)));
            Assert.Equal(new[] { "品名" + i }, Names(await HistoryGet(f, "suggestions", "kind=itemName&shopName=" + shop + "&categoryId=" + D(f.Category))));
            var fill = await HistoryGet(f, "autofill", "shopName=" + shop + "&transactionDate=2026-11-10"); Assert.Equal(D(f.Payment), fill["paymentMethodId"]!.GetValue<string>());
        }));
        Assert.Equal(before, f.Snapshot());
    }
}
