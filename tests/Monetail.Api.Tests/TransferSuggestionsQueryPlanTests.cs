using Xunit;
using static Monetail.Api.Tests.RecordApiTestData;

namespace Monetail.Api.Tests;

public sealed partial class TransferSuggestionsApiTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task TransferSuggestionsActualSqlFiltersLedgerThenGroupsBeforeLimitUsingExistingIndex(bool search)
    {
        await using var f = await RecordApiFixture.Create(); SeedTransferList(f, 1001); f.App.Database.Execute("UPDATE ledger_transfers SET name=printf('候補%04d',amount)");
        var capture = new TransferListCommandCapture(); using var host = HistoryHost(f, capture); using var client = f.App.Client(host); await f.App.LoginAsync(client); var before = f.Snapshot();
        var result = await TransferSuggestions(f, search ? "q=" + Uri.EscapeDataString("候補") : null, client: client); Assert.Equal(100, Names(result).Length); Assert.True(result["hasMore"]!.GetValue<bool>());
        Assert.NotNull(capture.Sql); Assert.Contains("GROUP BY t.name COLLATE BINARY", capture.Sql); Assert.Contains("ORDER BY MAX(t.updated_at_utc) DESC, Value COLLATE BINARY ASC LIMIT 101", capture.Sql);
        Assert.DoesNotContain("OFFSET", capture.Sql, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("COUNT(", capture.Sql, StringComparison.OrdinalIgnoreCase);
        using var connection = f.App.Database.OpenForeignKeyConnection();
        // EXPLAINのコンパイルにだけ必要。実検索はAPI接続の正規化関数を通して検証する。
        connection.CreateFunction<string, string, bool>("monetail_transfer_contains", (_, _) => throw new InvalidOperationException("plan function must not execute"), isDeterministic: true);
        using var command = connection.CreateCommand(); command.CommandText = "EXPLAIN QUERY PLAN " + capture.Sql;
        foreach (var (name, value) in capture.Parameters) command.Parameters.AddWithValue(name, value);
        var lines = new List<string>(); using (var reader = command.ExecuteReader()) while (reader.Read()) lines.Add(reader.GetString(3));
        Assert.Contains(lines, line => line.Contains("SEARCH t USING INDEX ix_ledger_transfers_transaction", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("TEMP B-TREE FOR GROUP BY", StringComparison.Ordinal)); Assert.Contains(lines, line => line.Contains("TEMP B-TREE FOR ORDER BY", StringComparison.Ordinal));
        Assert.Equal(before, f.Snapshot());
    }
}
