using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Features.Monetail.Transfers;
using Monetail.Api.Persistence;
using Xunit;

namespace Monetail.Api.Tests;

public sealed partial class ReceiptApiTests
{
    [Fact]
    public async Task TransferUpdateChangesBusinessValuesAndOnlyUpdateRevisionAndAudit()
    {
        await using var f = await Fixture.Create(); var location = await CreateTransfer(f, TransferInput(f)); var initial = await StoredTransfer(f);
        f.App.Time.Advance(TimeSpan.FromSeconds(2)); var input = await TransferUpdateInput(f, location); input["name"] = "  更新  "; input["amount"] = 123; input["memo"] = "メモ";
        var swap = input["fromPaymentMethodId"]!.GetValue<string>(); input["fromPaymentMethodId"] = input["toPaymentMethodId"]!.GetValue<string>(); input["toPaymentMethodId"] = swap;
        var others = f.App.Database.Tables().Except(["ledger_transfers", "operation_requests"]); var before = f.App.Database.Snapshot(others);
        await UpdateTransfer(f, location, input); var actual = await StoredTransfer(f);
        Assert.Equal(initial.Id, actual.Id); Assert.NotEqual(initial.Revision, actual.Revision); Assert.Equal(4, Guid.Parse(actual.Revision).Version);
        Assert.Equal(initial.CreatedAtUtc, actual.CreatedAtUtc); Assert.Equal(initial.CreatedByAuthUserId, actual.CreatedByAuthUserId);
        Assert.Equal(f.App.Time.GetUtcNow().UtcDateTime.Ticks, actual.UpdatedAtUtc.Ticks); Assert.Equal(f.App.UserId, actual.UpdatedByAuthUserId);
        Assert.Equal("  更新  ", actual.Name); Assert.Equal(123, actual.Amount); Assert.Equal("メモ", actual.Memo);
        Assert.Equal(initial.ToPaymentMethodId, actual.FromPaymentMethodId); Assert.Equal(initial.FromPaymentMethodId, actual.ToPaymentMethodId);
        Assert.Equal(before, f.App.Database.Snapshot(others)); Assert.Equal(1L, f.App.Database.Scalar("SELECT count(*) FROM operation_requests WHERE operation_type='transfer.update'"));
    }

    [Fact]
    public async Task TransferUpdateNoChangeDoesNotUpdateRowButAtomicallyRecordsSuccessAndReplays()
    {
        await using var f = await Fixture.Create(); var location = await CreateTransfer(f, TransferInput(f)); var input = await TransferUpdateInput(f, location); input["memo"] = "";
        f.App.Database.Execute("CREATE TRIGGER forbid_transfer_update BEFORE UPDATE ON ledger_transfers BEGIN SELECT RAISE(ABORT,'unexpected_update'); END");
        var business = f.App.Database.Snapshot(["ledger_transfers"]); await UpdateTransfer(f, location, input); Assert.Equal(business, f.App.Database.Snapshot(["ledger_transfers"]));
        Assert.Equal(1L, f.App.Database.Scalar("SELECT count(*) FROM operation_requests WHERE operation_type='transfer.update'"));
        input["memo"] = null; var before = f.Snapshot(); await UpdateTransfer(f, location, input); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferUpdateComparesNameExactlyAndTreatsStoredEmptyMemoAsNullWithoutUpdatingRow()
    {
        await using var f = await Fixture.Create(); var path = await CreateTransfer(f, TransferInput(f)); var input = await TransferUpdateInput(f, path);
        // 旧保存値を模擬する。API接続の制約は無効化しない。
        f.App.Database.Execute("PRAGMA ignore_check_constraints=ON; UPDATE ledger_transfers SET memo=''; PRAGMA ignore_check_constraints=OFF");
        f.App.Database.Execute("CREATE TRIGGER forbid_transfer_update BEFORE UPDATE ON ledger_transfers BEGIN SELECT RAISE(ABORT,'unexpected_update'); END");
        var before = f.App.Database.Snapshot(["ledger_transfers"]); await UpdateTransfer(f, path, input); Assert.Equal(before, f.App.Database.Snapshot(["ledger_transfers"]));
        f.App.Database.Execute("DROP TRIGGER forbid_transfer_update"); input = await TransferUpdateInput(f, path); input["name"] = " 振替 "; var revision = (await StoredTransfer(f)).Revision;
        await UpdateTransfer(f, path, input); Assert.NotEqual(revision, (await StoredTransfer(f)).Revision); Assert.Equal(" 振替 ", (await StoredTransfer(f)).Name);
    }

    [Fact]
    public async Task TransferUpdateChangesBothPaymentMethodsAndLocalDateWithInt32Maximum()
    {
        await using var f = await Fixture.Create(); var own = await OwnTransferPayment(f); var path = await CreateTransfer(f, TransferInput(f)); var input = await TransferUpdateInput(f, path);
        input["fromPaymentMethodId"] = D(own); input["toPaymentMethodId"] = D(f.Payment); input["transactionAt"] = "2026-12-31T23:59:59"; input["amount"] = int.MaxValue;
        await UpdateTransfer(f, path, input); var actual = await ReadTransfer(f.Client, path);
        foreach (var field in new[] { "fromPaymentMethodId", "toPaymentMethodId", "transactionAt", "amount" }) Assert.True(JsonNode.DeepEquals(input[field], actual[field]));
        Assert.Equal(DateTimeKind.Unspecified, (await StoredTransfer(f)).TransactionAt.Kind);
    }

    private static async Task<JsonObject> TransferUpdateInput(Fixture f, string location)
    {
        var input = await ReadTransfer(f.Client, location); input.Remove("transferId"); input["requestId"] = Guid.NewGuid().ToString("D"); return input;
    }
    private static async Task UpdateTransfer(Fixture f, string location, JsonObject input, HttpClient? client = null)
    {
        using var response = await (client ?? f.Client).PutAsJsonAsync(location, input); var body = await response.Content.ReadAsStringAsync(); Assert.True(response.StatusCode == HttpStatusCode.NoContent, body); Assert.Empty(body); Assert.Null(response.Headers.Location);
    }
    private static async Task<JsonObject> TransferUpdateError(Fixture f, string location, JsonObject input, int status, HttpClient? client = null)
    {
        using var response = await (client ?? f.Client).PutAsJsonAsync(location, input); var body = await response.Content.ReadAsStringAsync(); Assert.True((int)response.StatusCode == status, body); Assert.Null(response.Headers.Location); return JsonNode.Parse(body)!.AsObject();
    }
    private static async Task<LedgerTransfer> StoredTransfer(Fixture f)
    {
        await using var services = f.App.Database.CreateServices(); await using var scope = services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<ApiDbContext>().Set<LedgerTransfer>().AsNoTracking().SingleAsync();
    }
}
