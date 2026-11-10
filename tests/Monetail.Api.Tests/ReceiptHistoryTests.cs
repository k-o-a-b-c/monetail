using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Monetail.Api.Tests;

public sealed partial class ReceiptApiTests
{
    [Fact]
    public async Task HistoryShopSuggestionsNormalizeWithoutMergingOriginalSpellings()
    {
        await using var f = await Fixture.Create(); HistoryReceipt(f, 1, "スーパー"); HistoryReceipt(f, 2, "ｽｰﾊﾟｰ"); HistoryReceipt(f, 3, "スーパー");
        var before = f.Snapshot(); var result = await HistoryGet(f, "suggestions", "kind=shopName&q=" + Uri.EscapeDataString("すーぱー"));
        Keys(result, "suggestions", "hasMore"); Assert.Equal(new[] { "スーパー", "ｽｰﾊﾟｰ" }, Names(result)); Assert.False(result["hasMore"]!.GetValue<bool>()); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task HistoryLocationAndItemUseExactShopAndCategoryScopes()
    {
        await using var f = await Fixture.Create(); var id = HistoryReceipt(f, 1, "スーパー", location: "新宿");
        var other = HistoryReceipt(f, 2, "ｽｰﾊﾟｰ", location: "対象外");
        HistoryDetail(f, id, f.Category, "牛乳"); HistoryDetail(f, id, f.OtherCategory, "別科目"); HistoryDetail(f, other, f.Category, "別店舗");
        Assert.Equal(new[] { "新宿" }, Names(await HistoryGet(f, "suggestions", "kind=shopLocation&shopName=" + Uri.EscapeDataString("スーパー"))));
        Assert.Equal(new[] { "牛乳" }, Names(await HistoryGet(f, "suggestions", "kind=itemName&shopName=" + Uri.EscapeDataString("スーパー") + "&categoryId=" + D(f.Category))));
    }

    [Fact]
    public async Task HistoryAutofillSelectsTaxAndOwnedPaymentIndependentlyAndDoesNotWrite()
    {
        await using var f = await Fixture.Create(); f.Role("Editor", f.Owner);
        HistoryReceipt(f, 1, "店舗", updated: "2026-11-01 00:00:00"); HistoryReceipt(f, 2, "店舗", f.OtherPayment, updated: "2026-11-02 00:00:00", inclusion: 1, rounding: "half_up");
        var before = f.Snapshot(); var result = await HistoryGet(f, "autofill", "shopName=" + Uri.EscapeDataString("店舗") + "&transactionDate=2026-11-10");
        Keys(result, "paymentMethodId", "taxInclusionMode", "taxRoundingMode"); Assert.Equal(D(f.Payment), result["paymentMethodId"]!.GetValue<string>());
        Assert.Equal(1, result["taxInclusionMode"]!.GetValue<int>()); Assert.Equal("half_up", result["taxRoundingMode"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }

    private static string[] Names(JsonObject result) => result["suggestions"]!.AsArray().Select(p => p!.GetValue<string>()).ToArray();
    private static async Task<JsonObject> HistoryGet(Fixture f, string operation, string query, int status = 200, HttpClient? client = null)
    {
        using var response = await (client ?? f.Client).GetAsync($"/ledgers/{D(f.Ledger)}/receipt-{operation}?{query}");
        var text = await response.Content.ReadAsStringAsync(); Assert.True((int)response.StatusCode == status, $"Expected {status}, got {(int)response.StatusCode}: {text}");
        return JsonNode.Parse(text)!.AsObject();
    }
    private static string HistoryReceipt(Fixture f, int number, string shop, string? payment = null, string? location = null,
        string updated = "2026-11-10 12:00:00", int inclusion = 0, string rounding = "floor")
    {
        var id = SeedList(f, 1, payment, shop: shop, start: number - 1)[0];
        using var connection = f.App.Database.OpenForeignKeyConnection(); using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ledger_receipts SET shop_location=@location,updated_at_utc=@updated,tax_inclusion_mode=@inclusion,tax_rounding_mode=@rounding WHERE ledger_id=@ledger AND receipt_id=@id";
        foreach (var (key, value) in new (string, object)[] { ("@location", (object?)location ?? DBNull.Value), ("@updated", updated), ("@inclusion", inclusion), ("@rounding", rounding), ("@ledger", f.Ledger), ("@id", id) }) command.Parameters.AddWithValue(key, value);
        command.ExecuteNonQuery(); return id;
    }
    private static void HistoryDetail(Fixture f, string receipt, string category, string name)
    {
        using var connection = f.App.Database.OpenForeignKeyConnection(); using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ledger_receipt_groups(ledger_id,group_id,receipt_id,category_id,memo) VALUES(@ledger,@group,@receipt,@category,NULL)
            ON CONFLICT(ledger_id,receipt_id,category_id) DO NOTHING;
            INSERT INTO ledger_receipt_details(ledger_id,detail_id,group_id,display_order,name,unit_price,quantity,unit_discount_amount,discount_rate,tax_rate,expense_type,occurrence_type,memo)
            SELECT @ledger,@detail,group_id,0,@name,100,1,0,'0',NULL,'Variable','Irregular',NULL FROM ledger_receipt_groups
            WHERE ledger_id=@ledger AND receipt_id=@receipt AND category_id=@category
            """;
        foreach (var (key, value) in new[] { ("@ledger", f.Ledger), ("@group", Guid.NewGuid().ToString("N")), ("@receipt", receipt), ("@category", category),
            ("@detail", Guid.NewGuid().ToString("N")), ("@name", name) }) command.Parameters.AddWithValue(key, value);
        command.ExecuteNonQuery();
    }
}
