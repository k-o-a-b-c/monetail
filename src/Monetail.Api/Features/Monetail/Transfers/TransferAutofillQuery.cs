using System.Globalization;
using Monetail.Api.Features.Monetail.Ledgers;
using Monetail.Api.Features.Monetail.Receipts;

namespace Monetail.Api.Features.Monetail.Transfers;

internal sealed record TransferAutofillQuery(string Name, DateOnly Date, string? FromPaymentMethodId)
{
    internal static TransferAutofillQuery Read(IQueryCollection query)
    {
        foreach (var item in query)
            if (item.Key is not ("name" or "transactionDate" or "fromPaymentMethodId") || item.Value.Count != 1)
                throw LedgerPayload.Bad("検索項目を確認してください。", item.Key);
        var name = query["name"].ToString();
        if (name.Length is < 1 or > 100) throw LedgerPayload.Bad("1～100文字で指定してください。", "name");
        var text = query["transactionDate"].ToString();
        if (text.Length != 10 || !DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw LedgerPayload.Bad("取引日をYYYY-MM-DDで指定してください。", "transactionDate");
        var fromText = query.ContainsKey("fromPaymentMethodId") ? query["fromPaymentMethodId"].ToString() : null;
        if (fromText is not null && fromText.Length != 36) throw LedgerPayload.Bad("有効な識別子を指定してください。", "fromPaymentMethodId");
        var from = fromText is null ? null : ReceiptPayload.Uuid(fromText, "fromPaymentMethodId");
        return new(name, date, from);
    }
}
