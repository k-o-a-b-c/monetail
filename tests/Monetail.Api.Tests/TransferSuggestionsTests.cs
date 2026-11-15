using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Monetail.Api.Tests;

public sealed partial class ReceiptApiTests
{
    [Fact]
    public async Task TransferSuggestionsEmptyHistoryReturnsOnlyEmptyArrayAndFalseWithoutWriting()
    {
        await using var f = await Fixture.Create(); var before = f.Snapshot(); var result = await TransferSuggestions(f);
        Keys(result, "suggestions", "hasMore"); Assert.Empty(Names(result)); Assert.False(result["hasMore"]!.GetValue<bool>()); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferSuggestionsUseFullHistoryAndMaximumUpdateUtcForEachOriginalName()
    {
        await using var f = await Fixture.Create(); TransferCandidate(f, 1, "銀行へ入金", "2026-11-01 00:00:00"); TransferCandidate(f, 2, "銀行間振替", "2026-11-02 00:00:00"); TransferCandidate(f, 3, "銀行へ入金", "2026-11-03 00:00:00");
        var before = f.Snapshot(); var result = await TransferSuggestions(f); Keys(result, "suggestions", "hasMore");
        Assert.Equal(new[] { "銀行へ入金", "銀行間振替" }, Names(result)); Assert.False(result["hasMore"]!.GetValue<bool>());
        Assert.Equal(Names(result), Names(await TransferSuggestions(f, "q="))); Assert.Equal(new[] { "銀行へ入金" }, Names(await TransferSuggestions(f, "q=" + Uri.EscapeDataString("へ入"))));
        Assert.Empty(Names(await TransferSuggestions(f, "q=" + Uri.EscapeDataString("存在しない")))); Assert.Equal(before, f.Snapshot());
    }

    private static async Task<JsonObject> TransferSuggestions(Fixture f, string? query = null, int status = 200, HttpClient? client = null, string? ledger = null)
    {
        using var response = await (client ?? f.Client).GetAsync($"/ledgers/{D(ledger ?? f.Ledger)}/transfer-suggestions" + (query is null ? "" : "?" + query));
        var body = await response.Content.ReadAsStringAsync(); Assert.True((int)response.StatusCode == status, $"Expected {status}, got {(int)response.StatusCode}: {body}"); return JsonNode.Parse(body)!.AsObject();
    }
    private static string TransferCandidate(Fixture f, int number, string name, string updated = "2026-11-10 12:00:00")
    {
        var id = SeedTransferList(f, 1, name: name, start: number - 1)[0];
        using var connection = f.App.Database.OpenForeignKeyConnection(); using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ledger_transfers SET updated_at_utc=@updated WHERE ledger_id=@ledger AND transfer_id=@id";
        command.Parameters.AddWithValue("@updated", updated); command.Parameters.AddWithValue("@ledger", f.Ledger); command.Parameters.AddWithValue("@id", id); command.ExecuteNonQuery(); return id;
    }
}
