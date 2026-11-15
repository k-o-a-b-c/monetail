using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Monetail.Api.Authentication;
using Monetail.Api.Features.Monetail.Transfers;

namespace Monetail.Api.Features.Monetail.Ledgers;

public sealed partial class LedgerManagementService
{
    public Task<object> TransferSuggestionsAsync(string ledger, IQueryCollection query, CurrentSession actor, CancellationToken token) =>
        InLedgerAsync(ledger, actor, _ => true, async _ =>
        {
            var search = TransferSuggestionsQuery.Read(query);
            var predicate = "t.ledger_id=@ledger";
            var parameters = new List<object> { new SqliteParameter("@ledger", ledger) };
            if (search.Length > 0)
            {
                ((SqliteConnection)context.Database.GetDbConnection()).CreateFunction<string, string, bool>("monetail_transfer_contains", (name, value) =>
                    TransferSearchText.Normalize(name).Contains(value, StringComparison.Ordinal), isDeterministic: true);
                predicate += " AND monetail_transfer_contains(t.name,@search)=1";
                parameters.Add(new SqliteParameter("@search", TransferSearchText.Normalize(search)));
            }
            // 全履歴への検索と元文字列での重複排除後に、次候補の有無を含む101件を取得する。
            var sql = $"""
                SELECT t.name COLLATE BINARY AS Value FROM ledger_transfers t WHERE {predicate}
                GROUP BY t.name COLLATE BINARY ORDER BY MAX(t.updated_at_utc) DESC, Value COLLATE BINARY ASC LIMIT 101
                """;
            var rows = await context.Database.SqlQueryRaw<TransferSuggestionRow>(sql, parameters.ToArray()).ToArrayAsync(token);
            return new { suggestions = rows.Take(100).Select(row => row.Value).ToArray(), hasMore = rows.Length > 100 };
        }, token);
}

internal sealed class TransferSuggestionRow { public required string Value { get; init; } }
