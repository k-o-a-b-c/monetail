using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Monetail.Api.Features.Monetail.Ledgers;
using Monetail.Api.Features.Monetail.Receipts;

namespace Monetail.Api.Features.Monetail.Transfers;

/// <summary>共通の5項目検証を再利用し、振替専用purposeで継続位置を保護する。</summary>
internal sealed record TransferListQuery(ReceiptListQuery Filter)
{
    internal static TransferListQuery Read(IQueryCollection query) => new(ReceiptListQuery.Read(query));

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
