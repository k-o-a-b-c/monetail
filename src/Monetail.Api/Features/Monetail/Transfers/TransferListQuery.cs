using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Monetail.Api.Features.Monetail.Ledgers;
using Monetail.Api.Features.Monetail.Receipts;

namespace Monetail.Api.Features.Monetail.Transfers;

/// <summary>共通の5項目検証にフォーカス指定を加え、振替専用purposeで継続位置を保護する。</summary>
internal sealed record TransferListQuery(ReceiptListQuery Filter, string? FocusTransferId)
{
    internal static TransferListQuery Read(IQueryCollection query)
    {
        if (!query.TryGetValue("focusTransferId", out var focus)) return new(ReceiptListQuery.Read(query), null);
        if (focus.Count != 1 || query.ContainsKey("cursor"))
            throw LedgerPayload.Bad("フォーカス指定は1件とし、カーソルと同時に指定しないでください。", "focusTransferId");
        var value = focus.ToString();
        if (value.Length != 36) throw LedgerPayload.Bad("UUIDをD形式で指定してください。", "focusTransferId");
        var id = ReceiptPayload.Uuid(value, "focusTransferId");
        // 振替だけの追加項目を取り除き、レシート共通の5項目検証自体は変更しない。
        var remaining = new QueryCollection(query.Where(item => item.Key != "focusTransferId")
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
        return new(ReceiptListQuery.Read(remaining), id);
    }

    private string Fingerprint(string ledger) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { LedgerId = ledger, Filter.Month, Filter.OwnerPersonId, Filter.PaymentMethodId, Filter.Search })));
    private static IDataProtector Protector(IDataProtectionProvider provider) => provider.CreateProtector("Monetail.TransferList.Cursor.v1");
    internal TransferListPosition? Position(string ledger, IDataProtectionProvider provider)
    {
        if (Filter.Cursor is null) return null;
        try
        {
            var value = JsonSerializer.Deserialize<TransferListPosition>(Protector(provider).Unprotect(Filter.Cursor));
            if (value is null || value.LedgerId != ledger || value.Conditions != Fingerprint(ledger) ||
                !DateTime.TryParseExact(value.TransactionAt, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
                !Guid.TryParseExact(value.TransferId, "N", out var id) || id == Guid.Empty) throw InvalidCursor();
            return value;
        }
        catch (Exception error) when (error is CryptographicException or JsonException or FormatException or ArgumentException)
        { throw InvalidCursor(); }
    }
    internal string Next(string ledger, string at, string id, IDataProtectionProvider provider) =>
        Protector(provider).Protect(JsonSerializer.Serialize(new TransferListPosition(ledger, Fingerprint(ledger), at, id)));
    private static LedgerApiFault InvalidCursor() => new(400, "invalid_cursor", "続きの取得位置を確認できません。検索条件を指定し直してください。", "cursor");
}

internal sealed record TransferListPosition(string LedgerId, string Conditions, string TransactionAt, string TransferId);
