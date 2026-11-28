using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;
using static Monetail.Api.Tests.RecordApiTestData;

namespace Monetail.Api.Tests;

public sealed partial class TransferDeleteApiTests
{
    [Fact]
    public async Task TransferDeleteReplaySurvivesRestartAndUuidCaseChangesWithoutWriting()
    {
        await using var f = await RecordApiFixture.Create(); var path = await CreateTransfer(f, TransferInput(f)); var input = await TransferDeleteInput(f, path); await DeleteTransfer(f, path, input);
        using var host = f.App.NewFactory(); using var client = f.App.Client(host); await f.App.LoginAsync(client); await LoginTestApp.CsrfAsync(client);
        foreach (var field in new[] { "requestId", "revision" }) input[field] = input[field]!.GetValue<string>().ToUpperInvariant();
        var before = f.Snapshot(); await DeleteTransfer(f, path, input, client); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("Admin", true, 204)] [InlineData("Editor", true, 204)] [InlineData("Viewer", true, 403)] [InlineData("Editor", false, 404)]
    public async Task TransferDeleteReplayUsesCurrentRoleAndParticipationOnly(string role, bool member, int status)
    {
        await using var f = await RecordApiFixture.Create(); var path = await CreateTransfer(f, TransferInput(f)); var input = await TransferDeleteInput(f, path); await DeleteTransfer(f, path, input);
        if (member) f.Role(role, f.Other); else f.App.Database.Execute("DELETE FROM ledger_members"); var before = f.Snapshot();
        if (status == 204) await DeleteTransfer(f, path, input); else { var error = await TransferDeleteError(f, path, input, status); Assert.Null(error["latest"]); }
        Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferDeleteReplayRejectsExpiredAuthentication()
    {
        await using var f = await RecordApiFixture.Create(); var path = await CreateTransfer(f, TransferInput(f)); var input = await TransferDeleteInput(f, path); await DeleteTransfer(f, path, input); f.App.Time.Advance(TimeSpan.FromDays(15)); var before = f.Snapshot();
        using var response = await SendTransferDelete(f.Client, path, input); Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("revision")] [InlineData("transfer")] [InlineData("ledger")] [InlineData("operation")]
    public async Task TransferDeleteRequestIdBindsRevisionTargetLedgerAndOperation(string kind)
    {
        await using var f = await RecordApiFixture.Create(); var create = TransferInput(f); var path = await CreateTransfer(f, create); var input = await TransferDeleteInput(f, path);
        if (kind == "operation") input["requestId"] = create["requestId"]!.GetValue<string>();
        else
        {
            await DeleteTransfer(f, path, input);
            if (kind == "revision") input["revision"] = Guid.NewGuid().ToString("D");
            if (kind == "transfer") path = TransferRoot(f) + "/" + Guid.NewGuid();
            if (kind == "ledger")
            {
                using var response = await f.Client.PostAsJsonAsync("/ledgers", new { requestId = Guid.NewGuid(), name = "別家計簿" }); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                var ledger = (await response.Content.ReadFromJsonAsync<JsonObject>())!["ledgerId"]!.GetValue<string>(); path = $"/ledgers/{D(ledger)}/transfers/{path.Split('/').Last()}";
            }
        }
        var before = f.Snapshot(); var error = await TransferDeleteError(f, path, input, 409); Assert.Equal("request_conflict", error["code"]!.GetValue<string>()); Assert.Null(error["latest"]); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferDeleteRequestIdCannotBeReusedByAnotherActor()
    {
        await using var f = await RecordApiFixture.Create(); var path = await CreateTransfer(f, TransferInput(f)); var input = await TransferDeleteInput(f, path); await DeleteTransfer(f, path, input); var account = await f.App.AddAccountAsync();
        f.App.Database.Execute($"INSERT INTO ledger_members(ledger_id,auth_user_id,role,joined_at_utc,revision) VALUES('{f.Ledger}','{account.UserId}','Admin','2026-11-10','{Guid.NewGuid():N}')");
        using var client = f.App.Client(); var options = await f.App.BeginAsync(client); using var login = await f.App.VerifyAsync(client, f.App.AssertionFor(options, account.UserId, account.CredentialId)); Assert.Equal(HttpStatusCode.OK, login.StatusCode); await LoginTestApp.CsrfAsync(client); var before = f.Snapshot();
        Assert.Equal("request_conflict", (await TransferDeleteError(f, path, input, 409, client))["code"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task TransferDeleteConcurrentRequestsRemoveAndRecordOnlyOnce(bool sameRequest)
    {
        await using var f = await RecordApiFixture.Create(); var path = await CreateTransfer(f, TransferInput(f)); var a = await TransferDeleteInput(f, path); var b = a.DeepClone().AsObject(); if (!sameRequest) b["requestId"] = Guid.NewGuid().ToString("D");
        var responses = await Task.WhenAll(SendTransferDelete(f.Client, path, a), SendTransferDelete(f.Client, path, b));
        try
        {
            Assert.Equal(sameRequest ? new[] { 204, 204 } : [204, 404], responses.Select(r => (int)r.StatusCode).Order());
            foreach (var response in responses.Where(r => r.IsSuccessStatusCode)) Assert.Empty(await response.Content.ReadAsStringAsync());
        }
        finally { foreach (var response in responses) response.Dispose(); }
        Assert.Equal(0L, f.App.Database.Scalar("SELECT count(*) FROM ledger_transfers")); Assert.Equal(1L, f.App.Database.Scalar("SELECT count(*) FROM operation_requests WHERE operation_type='transfer.delete'"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task TransferDeleteAndPutHaveOnlyOneWinnerAndNeverPartiallyCommit(bool sameRequest)
    {
        await using var f = await RecordApiFixture.Create(); var path = await CreateTransfer(f, TransferInput(f)); var delete = await TransferDeleteInput(f, path); var update = await TransferUpdateInput(f, path); update["amount"] = 2; if (sameRequest) update["requestId"] = delete["requestId"]!.GetValue<string>();
        var responses = await Task.WhenAll(SendTransferDelete(f.Client, path, delete), f.Client.PutAsJsonAsync(path, update));
        try
        {
            var removed = responses[0].StatusCode == HttpStatusCode.NoContent;
            if (removed) Assert.Equal(sameRequest ? 409 : 404, (int)responses[1].StatusCode);
            else { Assert.Equal(HttpStatusCode.Conflict, responses[0].StatusCode); Assert.Equal(HttpStatusCode.NoContent, responses[1].StatusCode); Assert.Equal(2, (await StoredTransfer(f)).Amount); }
            var conflict = responses.FirstOrDefault(r => r.StatusCode == HttpStatusCode.Conflict);
            if (conflict is not null) Assert.Equal(sameRequest ? "request_conflict" : "revision_conflict", (await conflict.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>());
            Assert.Equal(removed ? 0L : 1L, f.App.Database.Scalar("SELECT count(*) FROM ledger_transfers"));
        }
        finally { foreach (var response in responses) response.Dispose(); }
        Assert.Equal(1L, f.App.Database.Scalar("SELECT count(*) FROM operation_requests WHERE operation_type IN ('transfer.delete','transfer.update')"));
    }
}
