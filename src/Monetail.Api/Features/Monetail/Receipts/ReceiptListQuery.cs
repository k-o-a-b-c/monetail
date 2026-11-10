using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Monetail.Api.Features.Monetail.Ledgers;

namespace Monetail.Api.Features.Monetail.Receipts;

/// <summary>一覧の条件と、永続Data Protectionキーで保護した継続位置を扱う。</summary>
internal sealed record ReceiptListQuery(string Month, string? OwnerPersonId, string? PaymentMethodId, string? Search,
    string? Cursor, string? From, string? To)
{
    internal static ReceiptListQuery Read(IQueryCollection query)
    {
        foreach (var item in query)
            if (!new[] { "month", "ownerPersonId", "paymentMethodId", "q", "cursor" }.Contains(item.Key, StringComparer.Ordinal) || item.Value.Count != 1)
                throw LedgerPayload.Bad("検索項目を確認してください。", item.Key);
        var month = query["month"].ToString(); string? from = null, to = null;
        if (month != "all")
        {
            if (month.Length != 7 || !DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw LedgerPayload.Bad("年月をYYYY-MMまたはallで指定してください。", "month");
            from = month + "-01T00:00:00";
            to = new DateTime(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month), 23, 59, 59)
                .ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        }
        string? Optional(string key) => query.TryGetValue(key, out var value) ? value.ToString() : null;
        string? Id(string key) => Optional(key) is string value ? ReceiptPayload.Uuid(value, key) : null;
        return new(month, Id("ownerPersonId"), Id("paymentMethodId"), Optional("q"), Optional("cursor"), from, to);
    }

    private string Fingerprint => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { Month, OwnerPersonId, PaymentMethodId, Search })));
    private static IDataProtector Protector(IDataProtectionProvider provider) => provider.CreateProtector("Monetail.ReceiptList.Cursor.v1");
    internal ReceiptListPosition? Position(string ledger, IDataProtectionProvider provider)
    {
        if (Cursor is null) return null;
        try
        {
            var value = JsonSerializer.Deserialize<ReceiptListPosition>(Protector(provider).Unprotect(Cursor));
            if (value is null || value.LedgerId != ledger || value.Conditions != Fingerprint ||
                !DateTime.TryParseExact(value.TransactionAt, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
                !Guid.TryParseExact(value.ReceiptId, "N", out var id) || id == Guid.Empty) throw InvalidCursor();
            return value;
        }
        catch (Exception error) when (error is CryptographicException or JsonException or FormatException or ArgumentException)
        { throw InvalidCursor(); }
    }
    internal string Next(string ledger, string at, string id, IDataProtectionProvider provider) =>
        Protector(provider).Protect(JsonSerializer.Serialize(new ReceiptListPosition(ledger, Fingerprint, at, id)));
    private static LedgerApiFault InvalidCursor() => new(400, "invalid_cursor", "続きの取得位置を確認できません。検索条件を指定し直してください。", "cursor");
}

internal sealed record ReceiptListPosition(string LedgerId, string Conditions, string TransactionAt, string ReceiptId);

internal static class ReceiptSearchText
{
    internal static string Normalize(string text)
    {
        // NFKC後に濁点を一時分解し、ヷ等もひらがなへ統一する。濁点・空白は除去しない。
        var characters = text.Normalize(NormalizationForm.FormKC).Normalize(NormalizationForm.FormD).ToCharArray();
        for (var i = 0; i < characters.Length; i++)
            if (characters[i] is >= '\u30a1' and <= '\u30f6' or '\u30fd' or '\u30fe') characters[i] = (char)(characters[i] - 0x60);
        return new string(characters).Normalize(NormalizationForm.FormC).ToUpperInvariant();
    }
}
