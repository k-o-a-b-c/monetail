using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Persistence;
using Xunit;
using static Monetail.Api.Tests.RecordApiTestData;

namespace Monetail.Api.Tests;

public sealed partial class TransferUpdateApiTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task TransferUpdateStaleRevisionReturnsExactlyDetailLatestWithoutChangingData(bool unchanged)
    {
        await using var f = await RecordApiFixture.Create(); var path = await CreateTransfer(f, TransferInput(f)); var latest = await ReadTransfer(f.Client, path); var input = await TransferUpdateInput(f, path); input["revision"] = Guid.NewGuid().ToString("D"); if (!unchanged) input["amount"] = 9;
        var before = f.Snapshot(); var error = await TransferUpdateError(f, path, input, 409); Assert.Equal("revision_conflict", error["code"]!.GetValue<string>());
        Keys(error["latest"]!.AsObject(), "transferId", "transactionAt", "name", "fromPaymentMethodId", "toPaymentMethodId", "amount", "memo", "revision");
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(latest, error["latest"])); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task TransferUpdateConcurrentRequestsHaveOneWinnerUnlessExactReplay(bool sameRequest)
    {
        await using var f = await RecordApiFixture.Create(); var path = await CreateTransfer(f, TransferInput(f)); var a = await TransferUpdateInput(f, path); a["amount"] = 2; var b = a.DeepClone().AsObject(); if (!sameRequest) { b["requestId"] = Guid.NewGuid().ToString("D"); b["amount"] = 3; }
        var responses = await Task.WhenAll(f.Client.PutAsJsonAsync(path, a), f.Client.PutAsJsonAsync(path, b));
        try { Assert.Equal(sameRequest ? new[] { 204, 204 } : [204, 409], responses.Select(r => (int)r.StatusCode).Order()); }
        finally { foreach (var response in responses) response.Dispose(); }
        Assert.Equal(1L, f.App.Database.Scalar("SELECT count(*) FROM operation_requests WHERE operation_type='transfer.update'")); Assert.Contains((await StoredTransfer(f)).Amount, new[] { 2, 3 });
    }

    [Theory]
    [InlineData("row")] [InlineData("record")] [InlineData("noop-record")]
    public async Task TransferUpdateFailureAtEitherWriteRollsBackEverything(string where)
    {
        await using var f = await RecordApiFixture.Create(); var path = await CreateTransfer(f, TransferInput(f)); var input = await TransferUpdateInput(f, path); if (where != "noop-record") input["amount"] = 2;
        f.App.Database.Execute(where == "row" ? "CREATE TRIGGER fail_transfer BEFORE UPDATE ON ledger_transfers BEGIN SELECT RAISE(ABORT,'synthetic_failure'); END" : "CREATE TRIGGER fail_transfer BEFORE INSERT ON operation_requests BEGIN SELECT RAISE(ABORT,'synthetic_failure'); END");
        var before = f.Snapshot(); Assert.Equal("store_unavailable", (await TransferUpdateError(f, path, input, 503))["code"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task TransferUpdateConditionallyChecksRevisionAtActualWrite(bool unchanged)
    {
        await using var f = await RecordApiFixture.Create(); var path = await CreateTransfer(f, TransferInput(f)); var input = await TransferUpdateInput(f, path); if (!unchanged) input["amount"] = 2;
        var mutation = new TransferUpdateWriteMutation(unchanged ? "INSERT INTO operation_requests" : "UPDATE \"ledger_transfers\"", "UPDATE ledger_transfers SET revision='" + Guid.NewGuid().ToString("N") + "'");
        using var host = f.App.NewFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddDbContext<ApiDbContext>(options => options.AddInterceptors(mutation))));
        using var client = f.App.Client(host); await f.App.LoginAsync(client); await LoginTestApp.CsrfAsync(client); var before = f.Snapshot(); mutation.Armed = true;
        Assert.Equal("revision_conflict", (await TransferUpdateError(f, path, input, 409, client))["code"]!.GetValue<string>()); Assert.False(mutation.Armed); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData(false, "role", 403)] [InlineData(true, "role", 403)]
    [InlineData(false, "mapping", 403)] [InlineData(true, "mapping", 403)]
    [InlineData(false, "member", 404)] [InlineData(true, "member", 404)]
    [InlineData(false, "session", 401)] [InlineData(true, "session", 401)]
    [InlineData(false, "payment-from", 400)] [InlineData(true, "payment-to", 400)]
    [InlineData(false, "person-from", 400)] [InlineData(true, "person-to", 400)]
    public async Task TransferUpdateRechecksRightsAndPeriodsAfterSuccessRecordAndRollsBack(bool unchanged, string change, int status)
    {
        await using var f = await RecordApiFixture.Create(); var own = await OwnTransferPayment(f); var create = TransferInput(f); create["toPaymentMethodId"] = D(own); var path = await CreateTransfer(f, create); var input = await TransferUpdateInput(f, path); if (!unchanged) input["amount"] = 2; f.Role("Editor", f.Owner);
        var sql = change switch
        {
            "role" => "UPDATE ledger_members SET role='Viewer'", "mapping" => $"UPDATE ledger_members SET person_id='{f.Other}'", "member" => "DELETE FROM ledger_members", "session" => "UPDATE auth_sessions SET RevokedAtUtc=CreatedAtUtc",
            "payment-from" => $"UPDATE ledger_payment_methods SET valid_to='2026-11-09' WHERE payment_method_id='{f.Payment}'", "payment-to" => $"UPDATE ledger_payment_methods SET valid_to='2026-11-09' WHERE payment_method_id='{own}'",
            _ => $"UPDATE ledger_people SET valid_to='2026-11-09' WHERE person_id='{f.Owner}'"
        };
        f.App.Database.Execute($"CREATE TRIGGER mutate_transfer AFTER INSERT ON operation_requests WHEN NEW.operation_type='transfer.update' BEGIN {sql}; END"); var before = f.Snapshot();
        var error = await TransferUpdateError(f, path, input, status); Assert.Null(error["latest"]); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("role", 403)] [InlineData("mapping", 403)] [InlineData("member", 404)] [InlineData("session", 401)] [InlineData("owner", 403)]
    public async Task TransferUpdateRechecksAccessBeforeExposingConflictLatest(string change, int status)
    {
        await using var f = await RecordApiFixture.Create(); var own = await OwnTransferPayment(f); var create = TransferInput(f); create["toPaymentMethodId"] = D(own); var path = await CreateTransfer(f, create); var input = await TransferUpdateInput(f, path); input["revision"] = Guid.NewGuid().ToString("D"); f.Role("Editor", f.Owner);
        var sql = change switch
        {
            "role" => "UPDATE ledger_members SET role='Viewer'", "mapping" => $"UPDATE ledger_members SET person_id='{f.Other}'", "member" => "DELETE FROM ledger_members", "session" => "UPDATE auth_sessions SET RevokedAtUtc=CreatedAtUtc",
            _ => $"DROP TRIGGER trg_ledger_payment_methods_owner_immutable; UPDATE ledger_payment_methods SET owner_person_id='{f.Other}',name='所有者変更検証' WHERE payment_method_id='{f.Payment}'"
        };
        var mutation = new TransferUpdateReadMutation(sql);
        using var host = f.App.NewFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddDbContext<ApiDbContext>(options => options.AddInterceptors(mutation))));
        using var client = f.App.Client(host); await f.App.LoginAsync(client); await LoginTestApp.CsrfAsync(client); var before = f.Snapshot(); mutation.Armed = true;
        using var response = await client.PutAsJsonAsync(path, input); Assert.Equal(status, (int)response.StatusCode); Assert.False(mutation.Armed); Assert.DoesNotContain("\"transferId\"", await response.Content.ReadAsStringAsync()); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferUpdateRechecksOwnerAfterRecordInsert()
    {
        await using var f = await RecordApiFixture.Create(); var own = await OwnTransferPayment(f); var create = TransferInput(f); create["toPaymentMethodId"] = D(own); var path = await CreateTransfer(f, create); var input = await TransferUpdateInput(f, path); input["amount"] = 2; f.Role("Editor", f.Owner);
        var mutation = new TransferOwnerMutation(f.Other, f.Payment);
        using var host = f.App.NewFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddDbContext<ApiDbContext>(options => options.AddInterceptors(mutation))));
        using var client = f.App.Client(host); await f.App.LoginAsync(client); await LoginTestApp.CsrfAsync(client); var before = f.Snapshot(); mutation.Armed = true;
        await TransferUpdateError(f, path, input, 403, client); Assert.False(mutation.Armed); Assert.Equal(before, f.Snapshot());
    }

}
