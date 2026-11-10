using System.Globalization;
using System.Text.Json;
using Monetail.Api.Features.Monetail.Ledgers;

namespace Monetail.Api.Features.Monetail.Receipts;

/// <summary>HTTPの型・省略・重複項目とD形式IDを検証する。業務値の検証は工程2へ渡す。</summary>
internal sealed record ReceiptPayload(string RequestId, string? Revision, string PaymentMethodId, string? Memo,
    ReceiptCalculationInput Calculation, ReceiptPayloadGroup[] Groups)
{
    internal static async Task<JsonElement> ReadAsync(HttpContext http)
    {
        if (!http.Request.HasJsonContentType()) throw Bad(null);
        try { return await http.Request.ReadFromJsonAsync<JsonElement>(http.RequestAborted); }
        catch (JsonException) { throw Bad(null); }
    }

    internal static ReceiptPayload Decode(JsonElement root, bool update)
    {
        var allowed = new[] { "requestId", "transactionAt", "shopName", "shopLocation", "paymentMethodId", "taxInclusionMode",
            "taxRoundingMode", "recurrenceInterval", "recurrenceUnit", "memo", "groups" }.Concat(update ? ["revision"] : System.Array.Empty<string>()).ToArray();
        Object(root, "", allowed);
        var request = Uuid(Value(root, "requestId"), "requestId");
        var revision = update ? Uuid(Value(root, "revision"), "revision") : null;
        var at = Text(root, "transactionAt"); var shop = Text(root, "shopName"); var location = Text(root, "shopLocation", nullable: true);
        var payment = Uuid(Value(root, "paymentMethodId"), "paymentMethodId");
        var inclusion = Integer(Value(root, "taxInclusionMode"), "taxInclusionMode");
        if (inclusion is < int.MinValue or > int.MaxValue) throw Bad("taxInclusionMode");
        var rounding = Text(root, "taxRoundingMode"); var intervalValue = Value(root, "recurrenceInterval");
        var interval = intervalValue.ValueKind == JsonValueKind.Null ? (long?)null : Integer(intervalValue, "recurrenceInterval");
        var unit = Text(root, "recurrenceUnit", nullable: true); var memo = Text(root, "memo", nullable: true);
        var groups = new List<ReceiptPayloadGroup>(); var index = 0;
        foreach (var value in Array(Value(root, "groups"), "groups"))
        {
            var path = $"groups[{index++}]";
            Object(value, path, "groupId", "categoryId", "memo", "details");
            var groupId = OptionalId(value, "groupId", path, update);
            var category = Uuid(Value(value, "categoryId", path), path + ".categoryId");
            var groupMemo = Text(value, "memo", path, nullable: true); var details = new List<ReceiptPayloadDetail>(); var j = 0;
            foreach (var line in Array(Value(value, "details", path), path + ".details"))
            {
                var field = $"{path}.details[{j++}]";
                Object(line, field, "detailId", "displayOrder", "name", "unitPrice", "quantity", "unitDiscountAmount", "discountRate", "taxRate", "expenseType", "occurrenceType", "memo", "personIds");
                var detailId = OptionalId(line, "detailId", field, update);
                var order = Integer(Value(line, "displayOrder", field), field + ".displayOrder");
                var name = Text(line, "name", field); var price = Integer(Value(line, "unitPrice", field), field + ".unitPrice");
                var quantity = Integer(Value(line, "quantity", field), field + ".quantity");
                var discount = Integer(Value(line, "unitDiscountAmount", field), field + ".unitDiscountAmount");
                var discountRate = Text(line, "discountRate", field); var tax = Text(line, "taxRate", field, nullable: true);
                var expense = Text(line, "expenseType", field); var occurrence = Text(line, "occurrenceType", field);
                var detailMemo = Text(line, "memo", field, nullable: true); var people = new List<string>(); var k = 0;
                foreach (var person in Array(Value(line, "personIds", field), field + ".personIds"))
                {
                    var personField = $"{field}.personIds[{k++}]"; var id = Uuid(person, personField);
                    if (people.Contains(id, StringComparer.Ordinal)) throw Bad(personField);
                    people.Add(id);
                }
                details.Add(new(detailId, detailMemo, people.ToArray(), new(name!, price, quantity, discount, discountRate!, tax, expense!, occurrence!, order)));
            }
            groups.Add(new(groupId, category, groupMemo, details.ToArray()));
        }
        return new(request, revision, payment, memo,
            new(at!, shop!, location, (int)inclusion, rounding!, interval, unit,
                groups.Select(g => new ReceiptCalculationGroup(Guid.ParseExact(g.CategoryId, "N"), g.Details.Select(d => d.Calculation).ToArray())).ToArray()), groups.ToArray());
    }

    internal static (string RequestId, string Revision) Delete(JsonElement root)
    {
        Object(root, "", "requestId", "revision");
        return (Uuid(Value(root, "requestId"), "requestId"), Uuid(Value(root, "revision"), "revision"));
    }
    internal static string Uuid(string value, string field) => Uuid(JsonSerializer.SerializeToElement(value), field);
    private static string Uuid(JsonElement value, string field)
    {
        if (value.ValueKind != JsonValueKind.String || !Guid.TryParseExact(value.GetString(), "D", out var id) || id == Guid.Empty) throw Bad(field);
        return id.ToString("N");
    }
    private static string? OptionalId(JsonElement value, string key, string path, bool update)
    {
        if (!value.TryGetProperty(key, out var id)) return null;
        if (!update) throw Bad(path + "." + key);
        return Uuid(id, path + "." + key);
    }
    private static void Object(JsonElement value, string path, params string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Bad(path);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!allowed.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name)) throw Bad(path.Length == 0 ? property.Name : path + "." + property.Name);
    }
    private static JsonElement Value(JsonElement value, string key, string path = "") => value.TryGetProperty(key, out var result) ? result : throw Bad(path.Length == 0 ? key : path + "." + key);
    private static string? Text(JsonElement value, string key, string path = "", bool nullable = false)
    {
        var item = Value(value, key, path);
        if (nullable && item.ValueKind == JsonValueKind.Null) return null;
        if (item.ValueKind != JsonValueKind.String) throw Bad(path.Length == 0 ? key : path + "." + key);
        return item.GetString();
    }
    private static IEnumerable<JsonElement> Array(JsonElement value, string path) => value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : throw Bad(path);
    private static long Integer(JsonElement value, string path) => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var result) ? result : throw Bad(path);
    private static LedgerApiFault Bad(string? field) => LedgerPayload.Bad("入力項目・型を確認してください。", field);

    internal string Canonical() => JsonSerializer.Serialize(new
    {
        Calculation.TransactionAt, Calculation.ShopName, Calculation.ShopLocation, PaymentMethodId, Calculation.TaxInclusionMode,
        Calculation.TaxRoundingMode, Calculation.RecurrenceInterval, Calculation.RecurrenceUnit, Memo,
        Groups = Groups.OrderBy(g => g.CategoryId, StringComparer.Ordinal).Select(g => new
        {
            g.Id, g.CategoryId, g.Memo,
            Details = g.Details.Select(d => JsonSerializer.Serialize(new
            {
                d.Id, d.Calculation.Name, d.Calculation.DisplayOrder, d.Calculation.UnitPrice, d.Calculation.Quantity, d.Calculation.UnitDiscountAmount,
                DiscountRate = Numeric(d.Calculation.DiscountRate), TaxRate = d.Calculation.TaxRate is null ? null : Numeric(d.Calculation.TaxRate),
                d.Calculation.ExpenseType, d.Calculation.OccurrenceType, d.Memo, PersonIds = d.PersonIds.Order(StringComparer.Ordinal)
            })).Order(StringComparer.Ordinal)
        })
    });
    private static string Numeric(string value)
    {
        try { return ReceiptValidation.DecimalValue(value, "").ToString("0.############################", CultureInfo.InvariantCulture); }
        catch (ReceiptValidationException) { return "invalid:" + value; }
    }
}

internal sealed record ReceiptPayloadGroup(string? Id, string CategoryId, string? Memo, ReceiptPayloadDetail[] Details);
internal sealed record ReceiptPayloadDetail(string? Id, string? Memo, string[] PersonIds, ReceiptCalculationDetail Calculation);
public sealed record ReceiptWriteResult(int Status, string ReceiptId);
