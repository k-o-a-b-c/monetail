using Monetail.Api.Features.Monetail.Ledgers;

namespace Monetail.Api.Features.Monetail.Transfers;

internal static class TransferSuggestionsQuery
{
    internal static string Read(IQueryCollection query)
    {
        foreach (var item in query)
            if (item.Key != "q" || item.Value.Count != 1)
                throw LedgerPayload.Bad("検索項目を確認してください。", item.Key);
        var search = query["q"].ToString();
        if (search.Length > 100) throw LedgerPayload.Bad("100文字以内で指定してください。", "q");
        return search;
    }
}
