using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Monetail.Api.Tests;

public sealed partial class ReceiptApiTests
{
    [Fact]
    public async Task TransferDeleteRemovesOnlyTargetAndRecordsSuccessInSameTransaction()
    {
        await using var f = await Fixture.Create(); var path = await CreateTransfer(f, TransferInput(f)); var receipt = await f.CreateReceipt(f.Input()); var input = await TransferDeleteInput(f, path);
        var keepInput = TransferInput(f); keepInput["name"] = "残す振替"; var keepPath = await CreateTransfer(f, keepInput); var keep = await ReadTransfer(f.Client, keepPath);
        var other = f.App.Database.Tables().Except(["ledger_transfers", "operation_requests"]); var before = f.App.Database.Snapshot(other);
        await DeleteTransfer(f, path, input); Assert.Equal(before, f.App.Database.Snapshot(other)); Assert.Equal(1L, f.App.Database.Scalar("SELECT count(*) FROM ledger_transfers"));
        Assert.Equal(1L, f.App.Database.Scalar("SELECT count(*) FROM operation_requests WHERE operation_type='transfer.delete'"));
        await ReadTransfer(f.Client, path, 404); Assert.True(JsonNode.DeepEquals(keep, await ReadTransfer(f.Client, keepPath)));
        var listed = Assert.Single((await TransferPage(f, "month=all"))["transfers"]!.AsArray()); Assert.Equal(keep["transferId"]!.GetValue<string>(), listed!["transferId"]!.GetValue<string>()); await f.Get(receipt);
    }

    [Fact]
    public async Task TransferDeleteSuccessReplayDoesNotReadDeletedPaymentsOrRepeatAnyWrite()
    {
        await using var f = await Fixture.Create(); var own = await OwnTransferPayment(f); var create = TransferInput(f); create["toPaymentMethodId"] = D(own); var path = await CreateTransfer(f, create); var input = await TransferDeleteInput(f, path);
        f.Role("Editor", f.Owner); await DeleteTransfer(f, path, input);
        f.App.Database.Execute($"DELETE FROM ledger_payment_methods WHERE owner_person_id='{f.Owner}'; UPDATE ledger_members SET person_id=NULL");
        f.App.Database.Execute("CREATE TRIGGER forbid_delete BEFORE DELETE ON ledger_transfers BEGIN SELECT RAISE(ABORT,'unexpected_delete'); END; CREATE TRIGGER forbid_record BEFORE INSERT ON operation_requests BEGIN SELECT RAISE(ABORT,'unexpected_record'); END");
        var before = f.Snapshot(); await DeleteTransfer(f, path, input); Assert.Equal(before, f.Snapshot());
    }

    private static async Task<JsonObject> TransferDeleteInput(Fixture f, string path) => new()
    {
        ["requestId"] = Guid.NewGuid().ToString("D"), ["revision"] = (await ReadTransfer(f.Client, path))["revision"]!.GetValue<string>()
    };
    private static async Task<HttpResponseMessage> SendTransferDelete(HttpClient client, string path, JsonObject input)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, path) { Content = JsonContent.Create(input) }; return await client.SendAsync(request);
    }
    private static async Task DeleteTransfer(Fixture f, string path, JsonObject input, HttpClient? client = null)
    {
        using var response = await SendTransferDelete(client ?? f.Client, path, input); var body = await response.Content.ReadAsStringAsync(); Assert.True(response.StatusCode == HttpStatusCode.NoContent, body); Assert.Empty(body); Assert.Null(response.Headers.Location);
    }
    private static async Task<JsonObject> TransferDeleteError(Fixture f, string path, JsonObject input, int status, HttpClient? client = null)
    {
        using var response = await SendTransferDelete(client ?? f.Client, path, input); var body = await response.Content.ReadAsStringAsync(); Assert.True((int)response.StatusCode == status, body); Assert.Null(response.Headers.Location); return JsonNode.Parse(body)!.AsObject();
    }
}
