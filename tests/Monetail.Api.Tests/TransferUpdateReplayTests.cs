using System.Net;
using System.Net.Http.Json;
using Xunit;
using static Monetail.Api.Tests.RecordApiTestData;

namespace Monetail.Api.Tests;

public sealed partial class TransferUpdateApiTests
{
    [Theory]
    [InlineData("amount")] [InlineData("name")] [InlineData("revision")] [InlineData("target")] [InlineData("operation")]
    public async Task TransferUpdateRequestIdBindsBusinessValuesRevisionTargetAndOperation(string change)
    {
        await using var f = await RecordApiFixture.Create(); var create = TransferInput(f); var path = await CreateTransfer(f, create); var input = await TransferUpdateInput(f, path); input["amount"] = 2;
        if (change == "operation") input["requestId"] = create["requestId"]!.GetValue<string>();
        else
        {
            await UpdateTransfer(f, path, input);
            if (change == "amount") input["amount"] = 3;
            if (change == "name") input["name"] = "異なる内容";
            if (change == "revision") input["revision"] = Guid.NewGuid().ToString("D");
            if (change == "target") path = TransferRoot(f) + "/" + Guid.NewGuid();
        }
        var before = f.Snapshot(); var error = await TransferUpdateError(f, path, input, 409); Assert.Equal("request_conflict", error["code"]!.GetValue<string>()); Assert.Null(error["latest"]); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferUpdateReplayDoesNotRepeatLaterBusinessChangesAndSurvivesRestart()
    {
        await using var f = await RecordApiFixture.Create(); var path = await CreateTransfer(f, TransferInput(f)); var first = await TransferUpdateInput(f, path); first["amount"] = 2; await UpdateTransfer(f, path, first);
        var next = await TransferUpdateInput(f, path); next["amount"] = 3; await UpdateTransfer(f, path, next);
        using var host = f.App.NewFactory(); using var client = f.App.Client(host); await f.App.LoginAsync(client); await LoginTestApp.CsrfAsync(client); var before = f.Snapshot();
        foreach (var field in new[] { "requestId", "revision", "fromPaymentMethodId", "toPaymentMethodId" }) first[field] = first[field]!.GetValue<string>().ToUpperInvariant();
        await UpdateTransfer(f, path, first, client); Assert.Equal(before, f.Snapshot()); Assert.Equal(3, (await StoredTransfer(f)).Amount);
    }

    [Theory]
    [InlineData("role", 403)] [InlineData("mapping", 403)] [InlineData("member", 404)] [InlineData("session", 401)]
    public async Task TransferUpdateReplayRequiresCurrentPermission(string change, int status)
    {
        await using var f = await RecordApiFixture.Create(); var own = await OwnTransferPayment(f); var create = TransferInput(f); create["toPaymentMethodId"] = D(own); var path = await CreateTransfer(f, create); var input = await TransferUpdateInput(f, path); input["amount"] = 2; f.Role("Editor", f.Owner); await UpdateTransfer(f, path, input);
        f.App.Database.Execute(change switch { "role" => "UPDATE ledger_members SET role='Viewer'", "mapping" => $"UPDATE ledger_members SET person_id='{f.Other}'", "member" => "DELETE FROM ledger_members", _ => "UPDATE auth_sessions SET RevokedAtUtc=CreatedAtUtc" }); var before = f.Snapshot();
        using var response = await f.Client.PutAsJsonAsync(path, input); Assert.Equal(status, (int)response.StatusCode); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferUpdateAnotherActorCannotReuseRequestAndCreationAuditIsPreserved()
    {
        await using var f = await RecordApiFixture.Create(); var path = await CreateTransfer(f, TransferInput(f)); var initial = await StoredTransfer(f); var input = await TransferUpdateInput(f, path); input["amount"] = 2; await UpdateTransfer(f, path, input);
        var account = await f.App.AddAccountAsync(); f.App.Database.Execute($"INSERT INTO ledger_members(ledger_id,auth_user_id,role,joined_at_utc,revision) VALUES('{f.Ledger}','{account.UserId}','Admin','2026-11-10','{Guid.NewGuid():N}')");
        using var client = f.App.Client(); var options = await f.App.BeginAsync(client); using var login = await f.App.VerifyAsync(client, f.App.AssertionFor(options, account.UserId, account.CredentialId)); Assert.Equal(HttpStatusCode.OK, login.StatusCode); await LoginTestApp.CsrfAsync(client);
        var before = f.Snapshot(); Assert.Equal("request_conflict", (await TransferUpdateError(f, path, input, 409, client))["code"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
        var next = await TransferUpdateInput(f, path); next["amount"] = 3; f.App.Time.Advance(TimeSpan.FromSeconds(5)); await UpdateTransfer(f, path, next, client); var actual = await StoredTransfer(f);
        Assert.Equal(initial.CreatedByAuthUserId, actual.CreatedByAuthUserId); Assert.Equal(initial.CreatedAtUtc, actual.CreatedAtUtc); Assert.Equal(account.UserId, actual.UpdatedByAuthUserId); Assert.Equal(f.App.Time.GetUtcNow().UtcDateTime.Ticks, actual.UpdatedAtUtc.Ticks);
    }

    [Fact]
    public async Task TransferUpdateUsesSharedTwentyWritesLimitIncludingPostAndOtherMasters()
    {
        await using var f = await RecordApiFixture.Create(); var path = await CreateTransfer(f, TransferInput(f)); var input = await TransferUpdateInput(f, path);
        for (var i = 0; i < 12; i++) { input["requestId"] = Guid.NewGuid().ToString("D"); await UpdateTransfer(f, path, input); }
        using var response = await f.Client.PutAsJsonAsync(path, input); Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        using var other = await f.Client.PostAsJsonAsync($"/ledgers/{D(f.Ledger)}/people", new { requestId = Guid.NewGuid(), name = "回数枠確認" }); Assert.Equal(HttpStatusCode.TooManyRequests, other.StatusCode);
        Assert.Equal(12L, f.App.Database.Scalar("SELECT count(*) FROM operation_requests WHERE operation_type='transfer.update'"));
    }

    [Theory]
    [InlineData("csrf")] [InlineData("origin")] [InlineData("anonymous")]
    public async Task TransferUpdateMaintainsExistingAuthenticationAndCsrfBoundaries(string kind)
    {
        await using var f = await RecordApiFixture.Create(); var path = await CreateTransfer(f, TransferInput(f)); var input = await TransferUpdateInput(f, path); using var client = f.App.Client();
        if (kind != "anonymous") await f.App.LoginAsync(client);
        if (kind == "origin") { await LoginTestApp.CsrfAsync(client); client.DefaultRequestHeaders.Remove("Origin"); client.DefaultRequestHeaders.Add("Origin", "https://invalid.example"); }
        var before = f.Snapshot(); using var response = await client.PutAsJsonAsync(path, input); Assert.Equal(kind == "anonymous" ? 401 : kind == "csrf" ? 400 : 403, (int)response.StatusCode); Assert.Equal(before, f.Snapshot());
    }
}
