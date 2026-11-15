using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Persistence;
using Xunit;

namespace Monetail.Api.Tests;

public sealed partial class ReceiptApiTests
{
    [Fact]
    public async Task TransferDeleteStaleRevisionReturnsSameEightFieldsAsDetailAndLeavesDatabaseUntouched()
    {
        await using var f = await Fixture.Create(); var path = await CreateTransfer(f, TransferInput(f)); var latest = await ReadTransfer(f.Client, path); var input = await TransferDeleteInput(f, path); input["revision"] = Guid.NewGuid().ToString("D"); var before = f.Snapshot();
        var error = await TransferDeleteError(f, path, input, 409); Assert.Equal("revision_conflict", error["code"]!.GetValue<string>()); Assert.True(JsonNode.DeepEquals(latest, error["latest"]));
        Keys(error["latest"]!.AsObject(), "transferId", "transactionAt", "name", "fromPaymentMethodId", "toPaymentMethodId", "amount", "memo", "revision"); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("revision", 409)] [InlineData("removed", 404)]
    public async Task TransferDeleteMatchesRevisionAtActualDeleteAndRollsBackOnZeroAffectedRows(string kind, int status)
    {
        await using var f = await Fixture.Create(); var path = await CreateTransfer(f, TransferInput(f)); var input = await TransferDeleteInput(f, path);
        var mutation = new TransferUpdateWriteMutation("DELETE FROM \"ledger_transfers\"", kind == "revision" ? $"UPDATE ledger_transfers SET revision='{Guid.NewGuid():N}'" : "DELETE FROM ledger_transfers");
        using var host = f.App.NewFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddDbContext<ApiDbContext>(options => options.AddInterceptors(mutation))));
        using var client = f.App.Client(host); await f.App.LoginAsync(client); await LoginTestApp.CsrfAsync(client); var before = f.Snapshot(); mutation.Armed = true;
        Assert.Equal(kind == "revision" ? "revision_conflict" : "not_found", (await TransferDeleteError(f, path, input, status, client))["code"]!.GetValue<string>()); Assert.False(mutation.Armed); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("row")] [InlineData("record")]
    public async Task TransferDeleteFailureAtEitherWriteRollsBackDeleteAndSuccessRecord(string where)
    {
        await using var f = await Fixture.Create(); var path = await CreateTransfer(f, TransferInput(f)); var input = await TransferDeleteInput(f, path);
        f.App.Database.Execute(where == "row" ? "CREATE TRIGGER fail_transfer_delete BEFORE DELETE ON ledger_transfers BEGIN SELECT RAISE(ABORT,'synthetic_failure'); END" : "CREATE TRIGGER fail_transfer_delete BEFORE INSERT ON operation_requests BEGIN SELECT RAISE(ABORT,'synthetic_failure'); END"); var before = f.Snapshot();
        Assert.Equal("store_unavailable", (await TransferDeleteError(f, path, input, 503))["code"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("row", "role", 403)] [InlineData("record", "role", 403)]
    [InlineData("row", "mapping", 403)] [InlineData("record", "mapping", 403)]
    [InlineData("row", "member", 404)] [InlineData("record", "member", 404)]
    [InlineData("row", "session", 401)] [InlineData("record", "session", 401)]
    public async Task TransferDeleteRechecksCurrentRightsAtBothWritesAndRollsBack(string where, string change, int status)
    {
        await using var f = await Fixture.Create(); var own = await OwnTransferPayment(f); var create = TransferInput(f); create["toPaymentMethodId"] = D(own); var path = await CreateTransfer(f, create); var input = await TransferDeleteInput(f, path); f.Role("Editor", f.Owner);
        var sql = change switch { "role" => "UPDATE ledger_members SET role='Viewer'", "mapping" => $"UPDATE ledger_members SET person_id='{f.Other}'", "member" => "DELETE FROM ledger_members", _ => "UPDATE auth_sessions SET RevokedAtUtc=CreatedAtUtc" };
        f.App.Database.Execute($"CREATE TRIGGER change_delete AFTER {(where == "row" ? "DELETE ON ledger_transfers" : "INSERT ON operation_requests WHEN NEW.operation_type='transfer.delete'")} BEGIN {sql}; END"); var before = f.Snapshot();
        var error = await TransferDeleteError(f, path, input, status); Assert.Null(error["latest"]); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task TransferDeleteRechecksBothOwnersAfterOperationRecordInsert(bool toSide)
    {
        await using var f = await Fixture.Create(); var own = await OwnTransferPayment(f); var create = TransferInput(f); create["toPaymentMethodId"] = D(own); var path = await CreateTransfer(f, create); var input = await TransferDeleteInput(f, path); f.Role("Editor", f.Owner);
        var mutation = new TransferOwnerMutation(f.Other, toSide ? own : f.Payment);
        using var host = f.App.NewFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddDbContext<ApiDbContext>(options => options.AddInterceptors(mutation))));
        using var client = f.App.Client(host); await f.App.LoginAsync(client); await LoginTestApp.CsrfAsync(client); var before = f.Snapshot(); mutation.Armed = true;
        await TransferDeleteError(f, path, input, 403, client); Assert.False(mutation.Armed); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("role", 403)] [InlineData("mapping", 403)] [InlineData("member", 404)] [InlineData("session", 401)] [InlineData("owner", 403)]
    public async Task TransferDeleteRechecksAccessBeforeExposingConflictLatest(string change, int status)
    {
        await using var f = await Fixture.Create(); var own = await OwnTransferPayment(f); var create = TransferInput(f); create["toPaymentMethodId"] = D(own); var path = await CreateTransfer(f, create); var input = await TransferDeleteInput(f, path); input["revision"] = Guid.NewGuid().ToString("D"); f.Role("Editor", f.Owner);
        var sql = change switch
        {
            "role" => "UPDATE ledger_members SET role='Viewer'", "mapping" => $"UPDATE ledger_members SET person_id='{f.Other}'", "member" => "DELETE FROM ledger_members", "session" => "UPDATE auth_sessions SET RevokedAtUtc=CreatedAtUtc",
            _ => $"DROP TRIGGER trg_ledger_payment_methods_owner_immutable; UPDATE ledger_payment_methods SET owner_person_id='{f.Other}',name='所有者変更検証' WHERE payment_method_id='{f.Payment}'"
        };
        var mutation = new TransferUpdateReadMutation(sql);
        using var host = f.App.NewFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddDbContext<ApiDbContext>(options => options.AddInterceptors(mutation))));
        using var client = f.App.Client(host); await f.App.LoginAsync(client); await LoginTestApp.CsrfAsync(client); var before = f.Snapshot(); mutation.Armed = true;
        using var response = await SendTransferDelete(client, path, input); Assert.Equal(status, (int)response.StatusCode); Assert.False(mutation.Armed); Assert.DoesNotContain("\"transferId\"", await response.Content.ReadAsStringAsync()); Assert.Equal(before, f.Snapshot());
    }
}
