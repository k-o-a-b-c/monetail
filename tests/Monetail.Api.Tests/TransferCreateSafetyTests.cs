using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Persistence;
using Xunit;
using static Monetail.Api.Tests.RecordApiTestData;

namespace Monetail.Api.Tests;

public sealed partial class TransferCreateApiTests
{
    [Theory]
    [InlineData("amount")] [InlineData("name")] [InlineData("memo")] [InlineData("operation")] [InlineData("ledger")]
    public async Task TransferCreateRequestIdCannotBeReusedForDifferentContentOperationOrLedger(string kind)
    {
        await using var f = await RecordApiFixture.Create(); var input = TransferInput(f); await CreateTransfer(f, input); var before = f.Snapshot();
        if (kind == "operation")
        {
            var receipt = f.Input(); receipt["requestId"] = input["requestId"]!.GetValue<string>(); Assert.Equal("request_conflict", (await f.Call("POST", receipt, 409))!["code"]!.GetValue<string>());
        }
        else if (kind == "ledger")
        {
            using var created = await f.Client.PostAsJsonAsync("/ledgers", new { requestId = Guid.NewGuid(), name = "別家計簿" }); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var other = (await created.Content.ReadFromJsonAsync<JsonObject>())!["ledgerId"]!.GetValue<string>(); before = f.Snapshot();
            using var response = await f.Client.PostAsJsonAsync($"/ledgers/{D(other)}/transfers", input); Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("request_conflict", (await response.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>());
        }
        else
        {
            if (kind == "amount") input["amount"] = 2; else input[kind] = "変更";
            Assert.Equal("request_conflict", (await TransferCreateError(f, input, 409))["code"]!.GetValue<string>());
        }
        Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferCreateDifferentActorCannotReuseSuccessRecord()
    {
        await using var f = await RecordApiFixture.Create(); var input = TransferInput(f); await CreateTransfer(f, input); var account = await f.App.AddAccountAsync();
        f.App.Database.Execute($"INSERT INTO ledger_members(ledger_id,auth_user_id,role,joined_at_utc,revision) VALUES('{f.Ledger}','{account.UserId}','Admin','2026-11-10','{Guid.NewGuid():N}')");
        using var client = f.App.Client(); var options = await f.App.BeginAsync(client); using var login = await f.App.VerifyAsync(client, f.App.AssertionFor(options, account.UserId, account.CredentialId)); Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        await LoginTestApp.CsrfAsync(client); var before = f.Snapshot();
        Assert.Equal("request_conflict", (await TransferCreateError(f, input, 409, client))["code"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferCreateConcurrentSameRequestCommitsOnlyOneTransferAndRecord()
    {
        await using var f = await RecordApiFixture.Create(); var input = TransferInput(f);
        var responses = await Task.WhenAll(f.Client.PostAsJsonAsync(TransferRoot(f), input), f.Client.PostAsJsonAsync(TransferRoot(f), input));
        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
            Assert.Equal(responses[0].Headers.Location, responses[1].Headers.Location); Assert.All(responses, response => Assert.NotNull(response.Headers.Location));
            foreach (var response in responses) Assert.Empty(await response.Content.ReadAsStringAsync());
        }
        finally { foreach (var response in responses) response.Dispose(); }
        Assert.Equal(1L, f.App.Database.Scalar("SELECT count(*) FROM ledger_transfers")); Assert.Equal(1L, f.App.Database.Scalar("SELECT count(*) FROM operation_requests WHERE operation_type='transfer.create'"));
    }

    [Fact]
    public async Task TransferCreateConcurrentRequestIdWithDifferentBodyHasOnlyOneWinner()
    {
        await using var f = await RecordApiFixture.Create(); var a = TransferInput(f); var b = a.DeepClone().AsObject(); b["amount"] = 2;
        var responses = await Task.WhenAll(f.Client.PostAsJsonAsync(TransferRoot(f), a), f.Client.PostAsJsonAsync(TransferRoot(f), b));
        try { Assert.Equal(new[] { 201, 409 }, responses.Select(r => (int)r.StatusCode).Order()); }
        finally { foreach (var response in responses) response.Dispose(); }
        Assert.Equal(1L, f.App.Database.Scalar("SELECT count(*) FROM ledger_transfers")); Assert.Equal(1L, f.App.Database.Scalar("SELECT count(*) FROM operation_requests WHERE operation_type='transfer.create'"));
    }

    [Fact]
    public async Task TransferCreateReplaySurvivesRestartAndDoesNotRecreateDeletedResult()
    {
        await using var f = await RecordApiFixture.Create(); var input = TransferInput(f); var location = await CreateTransfer(f, input); var id = Guid.Parse(location.Split('/').Last()).ToString("N");
        f.App.Database.Execute($"DELETE FROM ledger_transfers WHERE transfer_id='{id}'");
        using var host = f.App.NewFactory(); using var client = f.App.Client(host); await f.App.LoginAsync(client); await LoginTestApp.CsrfAsync(client); var before = f.Snapshot();
        Assert.Equal(location, await CreateTransfer(f, input, client)); Assert.Equal(before, f.Snapshot()); Assert.Equal(0L, f.App.Database.Scalar("SELECT count(*) FROM ledger_transfers"));
    }

    [Theory]
    [InlineData("role", 403)] [InlineData("mapping", 403)] [InlineData("member", 404)] [InlineData("session", 401)]
    public async Task TransferCreateReplayStillRequiresCurrentPermission(string change, int status)
    {
        await using var f = await RecordApiFixture.Create(); var input = TransferInput(f); input["toPaymentMethodId"] = D(await OwnTransferPayment(f)); f.Role("Editor", f.Owner); await CreateTransfer(f, input);
        f.App.Database.Execute(change switch { "role" => "UPDATE ledger_members SET role='Viewer'", "mapping" => $"UPDATE ledger_members SET person_id='{f.Other}'", "member" => "DELETE FROM ledger_members", _ => "UPDATE auth_sessions SET RevokedAtUtc=CreatedAtUtc" });
        var before = f.Snapshot(); using var response = await f.Client.PostAsJsonAsync(TransferRoot(f), input); Assert.Equal(status, (int)response.StatusCode); Assert.Null(response.Headers.Location); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("ledger_transfers")] [InlineData("operation_requests")]
    public async Task TransferCreateFailureAtEitherInsertRollsBackBothRecords(string table)
    {
        await using var f = await RecordApiFixture.Create(); var input = TransferInput(f); f.App.Database.Execute($"CREATE TRIGGER transfer_fail BEFORE INSERT ON {table} BEGIN SELECT RAISE(ABORT,'synthetic_failure'); END"); var before = f.Snapshot();
        Assert.Equal("store_unavailable", (await TransferCreateError(f, input, 503))["code"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("ledger_transfers", "role", 403)] [InlineData("operation_requests", "role", 403)]
    [InlineData("ledger_transfers", "mapping", 403)] [InlineData("operation_requests", "mapping", 403)]
    [InlineData("ledger_transfers", "member", 404)] [InlineData("operation_requests", "member", 404)]
    [InlineData("ledger_transfers", "session", 401)] [InlineData("operation_requests", "session", 401)]
    [InlineData("ledger_transfers", "payment-from", 400)] [InlineData("operation_requests", "payment-to", 400)]
    [InlineData("ledger_transfers", "person-from", 400)] [InlineData("operation_requests", "person-to", 400)]
    public async Task TransferCreateRechecksCurrentRightsAndBothMastersBeforeCommit(string table, string change, int status)
    {
        await using var f = await RecordApiFixture.Create(); var input = TransferInput(f); input["toPaymentMethodId"] = D(await OwnTransferPayment(f)); f.Role("Editor", f.Owner);
        var mutation = change switch
        {
            "role" => "UPDATE ledger_members SET role='Viewer'", "mapping" => $"UPDATE ledger_members SET person_id='{f.Other}'", "member" => "DELETE FROM ledger_members",
            "session" => "UPDATE auth_sessions SET RevokedAtUtc=CreatedAtUtc",
            "payment-from" => $"UPDATE ledger_payment_methods SET valid_to='2026-11-09' WHERE payment_method_id='{f.Payment}'",
            "payment-to" => $"UPDATE ledger_payment_methods SET valid_to='2026-11-09' WHERE payment_method_id='{Guid.Parse(input["toPaymentMethodId"]!.GetValue<string>()):N}'",
            _ => $"UPDATE ledger_people SET valid_to='2026-11-09' WHERE person_id='{f.Owner}'"
        };
        f.App.Database.Execute($"CREATE TRIGGER transfer_mutate AFTER INSERT ON {table} BEGIN {mutation}; END"); var before = f.Snapshot();
        var error = await TransferCreateError(f, input, status); Assert.Null(error["latest"]); Assert.False(error.ContainsKey("reason")); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferCreateRechecksOwnerAfterOperationRecordInsert()
    {
        await using var f = await RecordApiFixture.Create(); var input = TransferInput(f); input["toPaymentMethodId"] = D(await OwnTransferPayment(f)); f.Role("Editor", f.Owner);
        var mutation = new TransferOwnerMutation(f.Other, f.Payment);
        using var host = f.App.NewFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddDbContext<ApiDbContext>(options => options.AddInterceptors(mutation))));
        using var client = f.App.Client(host); await f.App.LoginAsync(client); await LoginTestApp.CsrfAsync(client); var before = f.Snapshot(); mutation.Armed = true;
        await TransferCreateError(f, input, 403, client); Assert.False(mutation.Armed); Assert.Equal(before, f.Snapshot());
    }

}
