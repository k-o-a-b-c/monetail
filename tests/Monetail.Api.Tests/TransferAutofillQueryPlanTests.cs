using Xunit;
using static Monetail.Api.Tests.RecordApiTestData;

namespace Monetail.Api.Tests;

public sealed partial class TransferAutofillApiTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task TransferAutofillSearchesAllHistoryBeforePickingUsablePairAndUsesExistingIndexes(bool editor, bool specified)
    {
        await using var f = await RecordApiFixture.Create(); var own = await OwnTransferPayment(f); AutofillTransfer(f, 1, f.Payment, own, updated: "2020-01-01 00:00:00");
        SeedTransferList(f, 1001, f.OtherPayment, f.Payment, start: 1000); f.App.Database.Execute($"UPDATE ledger_payment_methods SET valid_to='2026-11-09' WHERE payment_method_id='{f.OtherPayment}'"); if (editor) f.Role("Editor", f.Owner);
        var capture = new TransferListCommandCapture(); using var host = HistoryHost(f, capture); using var client = f.App.Client(host); await f.App.LoginAsync(client); var before = f.Snapshot();
        AssertTransferPair(await TransferAutofill(f, TransferAutofillQuery(from: specified ? D(f.Payment) : null), client: client), f.Payment, own);
        Assert.NotNull(capture.Sql); Assert.Contains("t.name COLLATE BINARY=@name", capture.Sql); Assert.Contains("ORDER BY t.updated_at_utc DESC,t.transfer_id COLLATE BINARY DESC LIMIT 1", capture.Sql);
        Assert.DoesNotContain("LIMIT 100", capture.Sql); Assert.DoesNotContain("OFFSET", capture.Sql, StringComparison.OrdinalIgnoreCase);
        using var connection = f.App.Database.OpenForeignKeyConnection(); using var command = connection.CreateCommand(); command.CommandText = "EXPLAIN QUERY PLAN " + capture.Sql;
        foreach (var (name, value) in capture.Parameters) command.Parameters.AddWithValue(name, value);
        var lines = new List<string>(); using (var reader = command.ExecuteReader()) while (reader.Read()) lines.Add(reader.GetString(3));
        Assert.Contains(lines, line => line.Contains("SEARCH t USING INDEX", StringComparison.Ordinal)); Assert.DoesNotContain(lines, line => line.StartsWith("SCAN ", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("TEMP B-TREE FOR ORDER BY", StringComparison.Ordinal)); Assert.Equal(before, f.Snapshot());
    }
}
