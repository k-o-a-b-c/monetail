using System.Globalization;
using System.Text.Json;
using Monetail.Api.Features.Monetail.Ledgers;
using Monetail.Api.Features.Monetail.Receipts;

namespace Monetail.Api.Features.Monetail.Transfers;

internal sealed record TransferCreateInput(string RequestId, DateTime TransactionAt, string Name,
    string FromPaymentMethodId, string ToPaymentMethodId, int Amount, string? Memo)
{
    internal static readonly string[] Fields = ["requestId", "transactionAt", "name", "fromPaymentMethodId", "toPaymentMethodId", "amount", "memo"];

    internal static TransferCreateInput Decode(LedgerPayload input)
    {
        var request = Uuid(input, "requestId"); var requestGuid = Guid.ParseExact(request, "N");
        if (requestGuid.Version != 4 || (requestGuid.ToByteArray()[8] & 0xc0) != 0x80) throw Bad("requestId");
        var text = input.Text("transactionAt");
        if (text.Length != 19 || !DateTime.TryParseExact(text, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)) throw Bad("transactionAt");
        var name = input.Text("name");
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100) throw Bad("name");
        var from = Uuid(input, "fromPaymentMethodId"); var to = Uuid(input, "toPaymentMethodId");
        if (from == to) throw Bad("toPaymentMethodId");
        var amountValue = input.Value("amount");
        if (amountValue.ValueKind != JsonValueKind.Number || !amountValue.TryGetInt32(out var amount) || amount < 1) throw Bad("amount");
        var memoValue = input.Value("memo");
        if (memoValue.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) throw Bad("memo");
        var memo = ReceiptPayload.NormalizeMemo(memoValue.ValueKind == JsonValueKind.Null ? null : memoValue.GetString());
        if (memo is not null && (memo.Length > 100 || memo.Any(character => character is '\r' or '\n' or '\v' or '\f' or '\u0085' or '\u2028' or '\u2029'))) throw Bad("memo");
        return new(request, at, name, from, to, amount, memo);
    }

    private static string Uuid(LedgerPayload input, string name)
    {
        var value = input.Text(name);
        if (value.Length != 36) throw Bad(name);
        return ReceiptPayload.Uuid(value, name);
    }
    private static LedgerApiFault Bad(string field) => LedgerPayload.Bad("入力項目・型・値を確認してください。", field);
    internal string Canonical() => JsonSerializer.Serialize(new
    {
        TransactionAt = TransactionAt.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture), Name, FromPaymentMethodId, ToPaymentMethodId, Amount, Memo
    });
}

public sealed record TransferCreatedResult(string TransferId);
