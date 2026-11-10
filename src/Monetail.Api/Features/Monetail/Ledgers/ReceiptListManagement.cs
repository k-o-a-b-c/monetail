using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Monetail.Api.Authentication;
using Monetail.Api.Features.Monetail.Receipts;

namespace Monetail.Api.Features.Monetail.Ledgers;

public sealed partial class LedgerManagementService
{
    public Task<object> ListReceiptsAsync(string ledger, IQueryCollection query, IDataProtectionProvider protection,
        CurrentSession actor, CancellationToken token) => InLedgerAsync(ledger, actor, _ => true, async _ =>
    {
        var filter = ReceiptListQuery.Read(query); var position = filter.Position(ledger, protection);
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        connection.CreateFunction<string, string, bool>("monetail_shop_contains", (name, search) =>
            ReceiptSearchText.Normalize(name).Contains(search, StringComparison.Ordinal), isDeterministic: true);
        var predicates = new List<string> { "r.ledger_id = @ledger" };
        var parameters = new List<object> { new SqliteParameter("@ledger", ledger) };
        void Add(string sql, string name, string value) { predicates.Add(sql); parameters.Add(new SqliteParameter(name, value)); }
        if (filter.From is not null)
        {
            Add("r.transaction_at >= @from", "@from", filter.From);
            Add("r.transaction_at <= @to", "@to", filter.To!);
        }
        if (filter.OwnerPersonId is not null)
            Add("EXISTS (SELECT 1 FROM ledger_payment_methods p WHERE p.ledger_id = r.ledger_id AND p.payment_method_id = r.payment_method_id AND p.owner_person_id = @owner)", "@owner", filter.OwnerPersonId);
        if (filter.PaymentMethodId is not null) Add("r.payment_method_id = @payment", "@payment", filter.PaymentMethodId);
        if (filter.Search is not null) Add("monetail_shop_contains(r.shop_name, @search) = 1", "@search", ReceiptSearchText.Normalize(filter.Search));
        if (position is not null)
        {
            // 行値比較により同日時のIDも含めたキーセット境界を索引へ渡す。OFFSETは使わない。
            predicates.Add("(r.transaction_at, r.receipt_id COLLATE BINARY) < (@at, @id)");
            parameters.Add(new SqliteParameter("@at", position.TransactionAt)); parameters.Add(new SqliteParameter("@id", position.ReceiptId));
        }
        var sql = $"""
            SELECT r.receipt_id AS ReceiptId, r.transaction_at AS TransactionAt, r.shop_name AS ShopName,
                   r.shop_location AS ShopLocation, r.payment_method_id AS PaymentMethodId, r.total_amount AS TotalAmount, r.memo AS Memo
            FROM ledger_receipts r WHERE {string.Join(" AND ", predicates)}
            ORDER BY r.transaction_at DESC, r.receipt_id COLLATE BINARY DESC LIMIT 101
            """;
        var rows = await context.Database.SqlQueryRaw<ReceiptListRow>(sql, parameters.ToArray()).ToArrayAsync(token);
        var page = rows.Take(100).Select(row => new
        {
            receiptId = D(row.ReceiptId), transactionAt = row.TransactionAt, shopName = row.ShopName, shopLocation = row.ShopLocation,
            paymentMethodId = D(row.PaymentMethodId), totalAmount = row.TotalAmount, memo = row.Memo
        }).ToArray();
        return new { receipts = page, nextCursor = rows.Length > 100 ? filter.Next(ledger, rows[99].TransactionAt, rows[99].ReceiptId, protection) : null };
    }, token);
}

internal sealed class ReceiptListRow
{
    public required string ReceiptId { get; init; }
    public required string TransactionAt { get; init; }
    public required string ShopName { get; init; }
    public string? ShopLocation { get; init; }
    public required string PaymentMethodId { get; init; }
    public int TotalAmount { get; init; }
    public string? Memo { get; init; }
}
