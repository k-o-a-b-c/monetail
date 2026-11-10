using System.Globalization;
using Monetail.Api.Features.Monetail.Ledgers;

namespace Monetail.Api.Features.Monetail.Receipts;

/// <summary>履歴検索の文字数・必須スコープと、余計な入力をAPI境界で検証する。</summary>
internal static class ReceiptHistoryQuery
{
    internal static (string Kind, string? Shop, string? Category, string Search) Suggestions(IQueryCollection query)
    {
        var kind = Text(query, "kind", required: true);
        var allowed = kind switch
        {
            "shopName" => new[] { "kind", "q" },
            "shopLocation" => ["kind", "shopName", "q"],
            "itemName" => ["kind", "shopName", "categoryId", "q"],
            _ => throw LedgerPayload.Bad("候補の種類を確認してください。", "kind")
        };
        Keys(query, allowed);
        var search = Text(query, "q") ?? "";
        var shop = kind == "shopName" ? null : Text(query, "shopName", required: true);
        if (shop == "") throw LedgerPayload.Bad("店名を指定してください。", "shopName");
        var category = kind == "itemName" ? ReceiptPayload.Uuid(Text(query, "categoryId", required: true)!, "categoryId") : null;
        return (kind!, shop, category, search);
    }

    internal static (string Shop, string Date) Autofill(IQueryCollection query)
    {
        Keys(query, "shopName", "transactionDate");
        var shop = Text(query, "shopName", required: true)!;
        var date = query["transactionDate"].ToString();
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw LedgerPayload.Bad("取引日をYYYY-MM-DDで指定してください。", "transactionDate");
        return (shop, date);
    }
    private static void Keys(IQueryCollection query, params string[] allowed)
    {
        foreach (var item in query)
            if (!allowed.Contains(item.Key, StringComparer.Ordinal) || item.Value.Count != 1)
                throw LedgerPayload.Bad("検索項目を確認してください。", item.Key);
    }
    private static string? Text(IQueryCollection query, string key, bool required = false)
    {
        if (!query.TryGetValue(key, out var value))
        {
            if (required) throw LedgerPayload.Bad("必要な検索条件を指定してください。", key);
            return null;
        }
        var text = value.ToString();
        if (key is "q" or "shopName" && text.Length > 100)
            throw LedgerPayload.Bad("100文字以内で指定してください。", key);
        return text;
    }
}
