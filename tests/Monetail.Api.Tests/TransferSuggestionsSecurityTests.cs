using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using static Monetail.Api.Tests.RecordApiTestData;

namespace Monetail.Api.Tests;

public sealed partial class TransferSuggestionsApiTests
{
    [Theory]
    [InlineData("Admin")] [InlineData("Editor")] [InlineData("Viewer")]
    public async Task TransferSuggestionsAllMembersCanReadOtherOwnersWithoutPersonMapping(string role)
    {
        await using var f = await RecordApiFixture.Create(); TransferCandidate(f, 1, "候補"); f.Role(role); var before = f.Snapshot();
        Assert.Equal(new[] { "候補" }, Names(await TransferSuggestions(f))); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("anonymous", 401)] [InlineData("expired", 401)] [InlineData("removed", 404)] [InlineData("unknown-ledger", 404)]
    public async Task TransferSuggestionsDoNotExposeHistoryWithoutCurrentAuthenticationAndMembership(string kind, int status)
    {
        await using var f = await RecordApiFixture.Create(); TransferCandidate(f, 1, "非公開の候補"); using var anonymous = f.App.Client();
        if (kind == "expired") f.App.Time.Advance(TimeSpan.FromDays(15)); if (kind == "removed") f.App.Database.Execute("DELETE FROM ledger_members"); var before = f.Snapshot();
        using var response = await (kind == "anonymous" ? anonymous : f.Client).GetAsync($"/ledgers/{(kind == "unknown-ledger" ? Guid.NewGuid().ToString("D") : D(f.Ledger))}/transfer-suggestions");
        Assert.Equal(status, (int)response.StatusCode); var text = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("非公開の候補", text); Assert.DoesNotContain("\"suggestions\"", text); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("member", 404)] [InlineData("expire", 401)] [InlineData("session", 401)]
    [InlineData("role", 200)] [InlineData("mapping", 200)] [InlineData("store", 503)]
    public async Task TransferSuggestionsRecheckCurrentAccessAfterReadingCandidates(string change, int status)
    {
        await using var f = await RecordApiFixture.Create(); TransferCandidate(f, 1, "候補"); f.Role("Admin", f.Owner); var mutation = new TransferSuggestionsReadMutation(f.App.Time, change, f.Other);
        using var host = HistoryHost(f, mutation); using var client = f.App.Client(host); await f.App.LoginAsync(client); var before = f.Snapshot(); mutation.Armed = true;
        var result = await TransferSuggestions(f, status: status, client: client); Assert.False(mutation.Armed);
        if (status == 200) Assert.Equal(new[] { "候補" }, Names(result));
        else { Assert.False(result.ContainsKey("suggestions")); Assert.Null(result["latest"]); Assert.DoesNotContain("synthetic-store-secret", result.ToJsonString()); Assert.Equal(before, f.Snapshot()); }
    }

    [Fact]
    public async Task TransferSuggestionsShareTheOneHundredTwentyReadQuotaWithTransferListAndPeople()
    {
        await using var f = await RecordApiFixture.Create(); var before = f.Snapshot();
        for (var i = 0; i < 60; i++) { await TransferSuggestions(f); await TransferPage(f, "month=all"); }
        using var candidate = await f.Client.GetAsync($"/ledgers/{D(f.Ledger)}/transfer-suggestions"); Assert.Equal(HttpStatusCode.TooManyRequests, candidate.StatusCode);
        using var people = await f.Client.GetAsync($"/ledgers/{D(f.Ledger)}/people"); Assert.Equal(HttpStatusCode.TooManyRequests, people.StatusCode); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferSuggestionsSeparateHouseholdsEvenWithSameTransferIds()
    {
        await using var f = await RecordApiFixture.Create(); TransferCandidate(f, 1, "公開名");
        using var ledger = await f.Client.PostAsJsonAsync("/ledgers", new { requestId = Guid.NewGuid(), name = "別家計簿" }); Assert.Equal(HttpStatusCode.Created, ledger.StatusCode);
        var other = (await ledger.Content.ReadFromJsonAsync<JsonObject>())!["ledgerId"]!.GetValue<string>();
        using var person = await f.Client.PostAsJsonAsync($"/ledgers/{D(other)}/people", new { requestId = Guid.NewGuid(), name = "別人物" }); Assert.Equal(HttpStatusCode.Created, person.StatusCode);
        var owner = (await person.Content.ReadFromJsonAsync<JsonObject>())!["people"]![0]!["personId"]!.GetValue<string>(); var payments = new List<string>();
        foreach (var name in new[] { "振替元", "振替先" })
        {
            using var payment = await f.Client.PostAsJsonAsync($"/ledgers/{D(other)}/payment-methods", new { requestId = Guid.NewGuid(), ownerPersonId = D(owner), name }); Assert.Equal(HttpStatusCode.Created, payment.StatusCode);
            payments.Add((await payment.Content.ReadFromJsonAsync<JsonObject>())!["paymentMethodId"]!.GetValue<string>());
        }
        SeedTransferList(f, 1, payments[0], payments[1], name: "別家計簿の名前", ledger: other); var before = f.Snapshot();
        Assert.Equal(new[] { "公開名" }, Names(await TransferSuggestions(f))); Assert.Equal(new[] { "別家計簿の名前" }, Names(await TransferSuggestions(f, ledger: other)));
        Assert.Empty(Names(await TransferSuggestions(f, "q=" + Uri.EscapeDataString("別家計簿")))); Assert.Equal(before, f.Snapshot());
        f.App.Database.Execute($"DELETE FROM ledger_members WHERE ledger_id='{other}'"); before = f.Snapshot();
        var denied = await TransferSuggestions(f, status: 404, ledger: other); Assert.False(denied.ContainsKey("suggestions")); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferSuggestionsConcurrentSearchesKeepTheirOwnParametersAndDoNotWrite()
    {
        await using var f = await RecordApiFixture.Create(); for (var i = 1; i <= 8; i++) TransferCandidate(f, i, "候補" + i); var before = f.Snapshot();
        await Task.WhenAll(Enumerable.Range(1, 8).Select(async i => Assert.Equal(new[] { "候補" + i }, Names(await TransferSuggestions(f, "q=" + i))))); Assert.Equal(before, f.Snapshot());
    }

    private sealed class TransferSuggestionsReadMutation(LoginTestApp.LoginTime clock, string change, string person) : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken token = default)
        {
            if (Armed && command.CommandText.Contains("AS Value FROM ledger_transfers t", StringComparison.Ordinal))
            {
                Armed = false;
                if (change == "store") throw new InvalidOperationException("synthetic-store-secret");
                if (change == "expire") clock.Advance(TimeSpan.FromDays(15));
                else
                {
                    using var mutate = command.Connection!.CreateCommand(); mutate.Transaction = command.Transaction;
                    mutate.CommandText = change switch { "member" => "DELETE FROM ledger_members", "session" => "UPDATE auth_sessions SET RevokedAtUtc=CreatedAtUtc", "role" => "UPDATE ledger_members SET role='Viewer'", _ => $"UPDATE ledger_members SET person_id='{person}'" }; await mutate.ExecuteNonQueryAsync(token);
                }
            }
            return result;
        }
    }
}
