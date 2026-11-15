using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Monetail.Api.Authentication;
using Monetail.Api.Features.Monetail.Transfers;

namespace Monetail.Api.Features.Monetail.Ledgers;

public sealed partial class LedgerManagementService
{
    public Task<object> ListTransfersAsync(string ledger, IQueryCollection query, IDataProtectionProvider protection,
        CurrentSession actor, CancellationToken token) => InLedgerAsync(ledger, actor, _ => true, async _ =>
    {
        var filter = TransferListQuery.Read(query); var position = filter.Position(ledger, protection); var search = filter.Filter;
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        connection.CreateFunction<string, string, bool>("monetail_transfer_contains", (name, value) =>
            TransferSearchText.Normalize(name).Contains(value, StringComparison.Ordinal), isDeterministic: true);
        var predicates = new List<string> { "t.ledger_id = @ledger" };
        var parameters = new List<object> { new SqliteParameter("@ledger", ledger) };
        void Add(string sql, string name, string value) { predicates.Add(sql); parameters.Add(new SqliteParameter(name, value)); }
        if (search.From is not null)
        {
            Add("t.transaction_at >= @from", "@from", search.From);
            Add("t.transaction_at <= @to", "@to", search.To!);
        }
        if (search.OwnerPersonId is not null)
            Add("EXISTS (SELECT 1 FROM ledger_payment_methods p WHERE p.ledger_id = t.ledger_id AND (p.payment_method_id = t.from_payment_method_id OR p.payment_method_id = t.to_payment_method_id) AND p.owner_person_id = @owner)", "@owner", search.OwnerPersonId);
        if (search.PaymentMethodId is not null)
            Add("(t.from_payment_method_id = @payment OR t.to_payment_method_id = @payment)", "@payment", search.PaymentMethodId);
        if (search.Search is not null)
            Add("monetail_transfer_contains(t.name, @search) = 1", "@search", TransferSearchText.Normalize(search.Search));
        if (position is not null)
        {
            predicates.Add("(t.transaction_at, t.transfer_id COLLATE BINARY) < (@at, @id)");
            parameters.Add(new SqliteParameter("@at", position.TransactionAt)); parameters.Add(new SqliteParameter("@id", position.TransferId));
        }
        var sql = $"""
            SELECT t.transfer_id AS TransferId, t.transaction_at AS TransactionAt, t.name AS Name,
                   t.from_payment_method_id AS FromPaymentMethodId, t.to_payment_method_id AS ToPaymentMethodId, t.amount AS Amount, t.memo AS Memo
            FROM ledger_transfers t WHERE {string.Join(" AND ", predicates)}
            ORDER BY t.transaction_at DESC, t.transfer_id COLLATE BINARY DESC LIMIT 101
            """;
        var rows = await context.Database.SqlQueryRaw<TransferListRow>(sql, parameters.ToArray()).ToArrayAsync(token);
        var page = rows.Take(100).Select(row => new
        {
            transferId = D(row.TransferId), transactionAt = row.TransactionAt, name = row.Name,
            fromPaymentMethodId = D(row.FromPaymentMethodId), toPaymentMethodId = D(row.ToPaymentMethodId), amount = row.Amount,
            memo = row.Memo == "" ? null : row.Memo
        }).ToArray();
        return new { transfers = page, nextCursor = rows.Length > 100 ? filter.Next(ledger, rows[99].TransactionAt, rows[99].TransferId, protection) : null };
    }, token);
}

internal sealed class TransferListRow
{
    public required string TransferId { get; init; }
    public required string TransactionAt { get; init; }
    public required string Name { get; init; }
    public required string FromPaymentMethodId { get; init; }
    public required string ToPaymentMethodId { get; init; }
    public int Amount { get; init; }
    public string? Memo { get; init; }
}
