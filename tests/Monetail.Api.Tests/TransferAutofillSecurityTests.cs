using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Monetail.Api.Tests;

public sealed partial class ReceiptApiTests
{
    [Theory]
    [InlineData("Viewer", true)] [InlineData("Editor", false)]
    public async Task TransferAutofillRejectsViewerAndUnmappedEditorBeforeReadingHistory(string role, bool mapped)
    {
        await using var f = await Fixture.Create(); TransferCandidate(f, 1, "振替"); f.Role(role, mapped ? f.Owner : null); var capture = new TransferListCommandCapture();
        using var host = HistoryHost(f, capture); using var client = f.App.Client(host); await f.App.LoginAsync(client); var before = f.Snapshot();
        var error = await TransferAutofill(f, status: 403, client: client); Assert.Equal("forbidden", error["code"]!.GetValue<string>()); Assert.Null(capture.Sql); Assert.False(error.ContainsKey("fromPaymentMethodId"));
        Assert.Equal(role, error["permissions"]!["currentRole"]!.GetValue<string>()); Assert.Equal(mapped ? D(f.Owner) : null, error["permissions"]!["myPersonId"]?.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferAutofillSpecifiedSourceRequiresCurrentEditorOwnershipEvenWithoutHistory()
    {
        await using var f = await Fixture.Create(); f.Role("Editor", f.Owner); var before = f.Snapshot();
        var error = await TransferAutofill(f, TransferAutofillQuery(from: D(f.OtherPayment)), 403); Assert.Equal("forbidden", error["code"]!.GetValue<string>()); Assert.False(error.ContainsKey("fromPaymentMethodId"));
        Assert.Equal(D(f.Owner), error["permissions"]!["myPersonId"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("anonymous", 401)] [InlineData("expired", 401)] [InlineData("member", 404)] [InlineData("unknown-ledger", 404)]
    public async Task TransferAutofillRequiresCurrentAuthenticationAndLedgerParticipation(string change, int status)
    {
        await using var f = await Fixture.Create(); TransferCandidate(f, 1, "振替"); using var anonymous = f.App.Client();
        if (change == "expired") f.App.Time.Advance(TimeSpan.FromDays(15)); if (change == "member") f.App.Database.Execute("DELETE FROM ledger_members"); var before = f.Snapshot();
        using var response = await (change == "anonymous" ? anonymous : f.Client).GetAsync($"/ledgers/{(change == "unknown-ledger" ? Guid.NewGuid().ToString("D") : D(f.Ledger))}/transfer-autofill?" + TransferAutofillQuery());
        Assert.Equal(status, (int)response.StatusCode); var text = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("\"fromPaymentMethodId\"", text); Assert.DoesNotContain("\"toPaymentMethodId\"", text); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("history", "viewer", 403)] [InlineData("source", "viewer", 403)]
    [InlineData("history", "unmap", 403)] [InlineData("source", "unmap", 403)]
    [InlineData("history", "person", 200)] [InlineData("source", "person", 200)]
    [InlineData("history", "admin", 200)] [InlineData("source", "admin", 200)]
    [InlineData("history", "editor", 200)] [InlineData("source", "editor", 200)]
    [InlineData("history", "member", 404)] [InlineData("source", "member", 404)]
    [InlineData("history", "expire", 401)] [InlineData("source", "expire", 401)]
    [InlineData("history", "session", 401)] [InlineData("source", "session", 401)]
    [InlineData("history", "store", 503)]
    public async Task TransferAutofillRechecksContextDiscardsOldPairsAndNeverResearches(string phase, string change, int status)
    {
        await using var f = await Fixture.Create(); var own = await OwnTransferPayment(f); var initialRole = change is "viewer" or "editor" or "member" or "expire" or "session" or "store" ? "Admin" : "Editor";
        var to = change == "editor" ? f.OtherPayment : own; AutofillTransfer(f, 1, f.Payment, to); f.Role(initialRole, f.Owner);
        var mutation = new TransferAutofillReadMutation(f.App.Time, phase, change, f.Other); using var host = HistoryHost(f, mutation); using var client = f.App.Client(host); await f.App.LoginAsync(client);
        var before = f.Snapshot(); var others = f.App.Database.Tables().Except(["ledger_members"]); var otherBefore = f.App.Database.Snapshot(others); mutation.Armed = true;
        var result = await TransferAutofill(f, TransferAutofillQuery(from: phase == "source" ? D(f.Payment) : null), status, client); Assert.False(mutation.Armed); Assert.Equal(1, mutation.HistoryQueries);
        if (status == 200) { AssertTransferPair(result, null, null); Assert.Equal(otherBefore, f.App.Database.Snapshot(others)); }
        else { Assert.False(result.ContainsKey("fromPaymentMethodId")); Assert.Null(result["latest"]); Assert.DoesNotContain("synthetic-autofill-secret", result.ToJsonString()); Assert.Equal(before, f.Snapshot()); }
    }

    [Fact]
    public async Task TransferAutofillDiscardsPairAfterAdminPersonMappingChangesEvenThoughAllPaymentsRemainAllowed()
    {
        await using var f = await Fixture.Create(); TransferCandidate(f, 1, "振替"); f.Role("Admin", f.Owner); var mutation = new TransferAutofillReadMutation(f.App.Time, "history", "person", f.Other);
        using var host = HistoryHost(f, mutation); using var client = f.App.Client(host); await f.App.LoginAsync(client); mutation.Armed = true;
        AssertTransferPair(await TransferAutofill(f, client: client), null, null); Assert.False(mutation.Armed); Assert.Equal(1, mutation.HistoryQueries);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task TransferAutofillStillRechecksRoleWhenHistoryIsEmptyOrSpecifiedSourceIsExpired(bool invalidSource)
    {
        await using var f = await Fixture.Create(); if (invalidSource) f.App.Database.Execute($"UPDATE ledger_payment_methods SET valid_to='2026-11-09' WHERE payment_method_id='{f.Payment}'");
        var mutation = new TransferAutofillReadMutation(f.App.Time, invalidSource ? "source" : "history", "viewer", f.Other);
        using var host = HistoryHost(f, mutation); using var client = f.App.Client(host); await f.App.LoginAsync(client); var before = f.Snapshot(); mutation.Armed = true;
        await TransferAutofill(f, TransferAutofillQuery(from: invalidSource ? D(f.Payment) : null), 403, client); Assert.False(mutation.Armed); Assert.Equal(invalidSource ? 0 : 1, mutation.HistoryQueries); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferAutofillSharesReadLimitWithSuggestionsAndOtherLedgerReads()
    {
        await using var f = await Fixture.Create(); var before = f.Snapshot();
        for (var i = 0; i < 60; i++) { await TransferAutofill(f); await TransferSuggestions(f); }
        using var fill = await f.Client.GetAsync($"/ledgers/{D(f.Ledger)}/transfer-autofill?" + TransferAutofillQuery()); Assert.Equal(HttpStatusCode.TooManyRequests, fill.StatusCode);
        using var list = await f.Client.GetAsync(TransferRoot(f) + "?month=all"); Assert.Equal(HttpStatusCode.TooManyRequests, list.StatusCode); Assert.Equal(before, f.Snapshot());
    }

    private sealed class TransferAutofillReadMutation(LoginTestApp.LoginTime clock, string phase, string change, string other) : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public int HistoryQueries { get; private set; }
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken token = default)
        {
            var history = command.CommandText.Contains("FROM ledger_transfers t", StringComparison.Ordinal); if (history) HistoryQueries++;
            if (Armed && (phase == "history" ? history : command.CommandText.Contains("FROM \"ledger_payment_methods\"", StringComparison.Ordinal)))
            {
                Armed = false; if (change == "store") throw new InvalidOperationException("synthetic-autofill-secret");
                if (change == "expire") clock.Advance(TimeSpan.FromDays(15));
                else
                {
                    using var mutate = command.Connection!.CreateCommand(); mutate.Transaction = command.Transaction;
                    mutate.CommandText = change switch { "viewer" => "UPDATE ledger_members SET role='Viewer'", "editor" => "UPDATE ledger_members SET role='Editor'", "admin" => "UPDATE ledger_members SET role='Admin'", "unmap" => "UPDATE ledger_members SET person_id=NULL", "person" => $"UPDATE ledger_members SET person_id='{other}'", "member" => "DELETE FROM ledger_members", _ => "UPDATE auth_sessions SET RevokedAtUtc=CreatedAtUtc" }; await mutate.ExecuteNonQueryAsync(token);
                }
            }
            return result;
        }
    }
}
