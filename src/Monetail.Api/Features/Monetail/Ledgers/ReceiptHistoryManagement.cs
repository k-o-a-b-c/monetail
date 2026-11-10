using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Monetail.Api.Authentication;
using Monetail.Api.Features.Monetail.Receipts;

namespace Monetail.Api.Features.Monetail.Ledgers;

public sealed partial class LedgerManagementService
{
    public Task<object> ReceiptSuggestionsAsync(string ledger, IQueryCollection query, CurrentSession actor, CancellationToken token) =>
        InLedgerAsync(ledger, actor, _ => true, async _ =>
        {
            var (kind, shop, category, search) = ReceiptHistoryQuery.Suggestions(query);
            var name = kind switch { "shopName" => "r.shop_name", "shopLocation" => "r.shop_location", _ => "d.name" };
            var from = "ledger_receipts r";
            // CROSS JOINでループ順を固定し、店名/科目で絞る前に全明細を正規化する計画を避ける。
            if (kind == "itemName") from += " CROSS JOIN ledger_receipt_groups g ON g.ledger_id=r.ledger_id AND g.receipt_id=r.receipt_id CROSS JOIN ledger_receipt_details d ON d.ledger_id=g.ledger_id AND d.group_id=g.group_id";
            var conditions = new List<string> { "r.ledger_id=@ledger", name + " IS NOT NULL" };
            var parameters = new List<object> { new SqliteParameter("@ledger", ledger) };
            if (shop is not null) { conditions.Add("r.shop_name COLLATE BINARY=@shop"); parameters.Add(new SqliteParameter("@shop", shop)); }
            if (category is not null) { conditions.Add("g.category_id=@category"); parameters.Add(new SqliteParameter("@category", category)); }
            if (search.Length > 0)
            {
                ((SqliteConnection)context.Database.GetDbConnection()).CreateFunction<string?, string, bool>("monetail_history_contains", (value, q) =>
                    value is not null && ReceiptSearchText.Normalize(value).Contains(q, StringComparison.Ordinal), isDeterministic: true);
                conditions.Add($"monetail_history_contains({name},@search)=1"); parameters.Add(new SqliteParameter("@search", ReceiptSearchText.Normalize(search)));
            }
            // NULLは文字列候補ではない。未確定の空文字/空白だけの文字列の除外処理は追加しない。
            var sql = $"""
                SELECT {name} COLLATE BINARY AS Value FROM {from} WHERE {string.Join(" AND ", conditions)}
                GROUP BY {name} COLLATE BINARY ORDER BY MAX(r.updated_at_utc) DESC, Value COLLATE BINARY ASC LIMIT 101
                """;
            var rows = await context.Database.SqlQueryRaw<ReceiptSuggestionRow>(sql, parameters.ToArray()).ToArrayAsync(token);
            return new { suggestions = rows.Take(100).Select(row => row.Value).ToArray(), hasMore = rows.Length > 100 };
        }, token);

    public async Task<object> ReceiptAutofillAsync(string ledger, IQueryCollection query, CurrentSession actor, CancellationToken token)
    {
        // 最後の参加照合の結果で支払方法だけを破棄するため、ここで応答を確定する。共通APIの契約は変えない。
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        var initial = await RequireMemberAsync(ledger, actor, _ => true, token);
        var (shop, date) = ReceiptHistoryQuery.Autofill(query);
        var tax = await context.Database.SqlQueryRaw<ReceiptAutofillTaxRow>("""
            SELECT r.tax_inclusion_mode AS TaxInclusionMode,r.tax_rounding_mode AS TaxRoundingMode
            FROM ledger_receipts r WHERE r.ledger_id=@ledger AND r.shop_name COLLATE BINARY=@shop
            ORDER BY r.updated_at_utc DESC,r.receipt_id COLLATE BINARY DESC LIMIT 1
            """, new SqliteParameter("@ledger", ledger), new SqliteParameter("@shop", shop)).ToArrayAsync(token);
        string? payment = null;
        if (initial.PersonId is not null && initial.Role is "Admin" or "Editor")
        {
            var rows = await context.Database.SqlQueryRaw<ReceiptAutofillPaymentRow>("""
                SELECT r.payment_method_id AS PaymentMethodId FROM ledger_receipts r
                JOIN ledger_payment_methods p ON p.ledger_id=r.ledger_id AND p.payment_method_id=r.payment_method_id
                JOIN ledger_people person ON person.ledger_id=p.ledger_id AND person.person_id=p.owner_person_id
                WHERE r.ledger_id=@ledger AND r.shop_name COLLATE BINARY=@shop AND p.owner_person_id=@person
                  AND (p.valid_from IS NULL OR p.valid_from<=@date) AND (p.valid_to IS NULL OR p.valid_to>=@date)
                  AND (person.valid_from IS NULL OR person.valid_from<=@date) AND (person.valid_to IS NULL OR person.valid_to>=@date)
                ORDER BY r.updated_at_utc DESC,r.receipt_id COLLATE BINARY DESC LIMIT 1
                """, new SqliteParameter("@ledger", ledger), new SqliteParameter("@shop", shop),
                new SqliteParameter("@person", initial.PersonId), new SqliteParameter("@date", date)).ToArrayAsync(token);
            if (rows.Length > 0) payment = D(rows[0].PaymentMethodId);
        }
        var current = await RequireMemberAsync(ledger, actor, _ => true, token);
        if (current.PersonId != initial.PersonId || current.Role != initial.Role) payment = null;
        var result = new { paymentMethodId = payment, taxInclusionMode = tax.FirstOrDefault()?.TaxInclusionMode, taxRoundingMode = tax.FirstOrDefault()?.TaxRoundingMode };
        await transaction.CommitAsync(token);
        return result;
    }
}

internal sealed class ReceiptSuggestionRow { public required string Value { get; init; } }
internal sealed class ReceiptAutofillTaxRow
{
    public int TaxInclusionMode { get; init; }
    public required string TaxRoundingMode { get; init; }
}
internal sealed class ReceiptAutofillPaymentRow { public required string PaymentMethodId { get; init; } }
