using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;
using static Monetail.Api.Tests.RecordApiTestData;

namespace Monetail.Api.Tests;

public sealed partial class TransferDeleteApiTests
{
    [Theory]
    [InlineData("Admin", "cross", 204)] [InlineData("Editor", "own", 204)] [InlineData("Editor", "from-other", 403)] [InlineData("Editor", "to-other", 403)]
    [InlineData("Editor", "unmapped", 403)] [InlineData("Viewer", "own", 403)]
    public async Task TransferDeleteChecksBothOwnersAndLedgerRole(string role, string mode, int status)
    {
        await using var f = await RecordApiFixture.Create(); var own = await OwnTransferPayment(f); var create = TransferInput(f); create["toPaymentMethodId"] = D(own);
        if (mode == "from-other") create["fromPaymentMethodId"] = D(f.OtherPayment);
        if (mode == "to-other" || mode == "cross") create["toPaymentMethodId"] = D(f.OtherPayment);
        var path = await CreateTransfer(f, create); var input = await TransferDeleteInput(f, path); f.Role(role, mode == "unmapped" ? null : f.Owner); var before = f.Snapshot();
        if (status == 204) await DeleteTransfer(f, path, input);
        else { var error = await TransferDeleteError(f, path, input, status); Assert.Equal("forbidden", error["code"]!.GetValue<string>()); Assert.Null(error["latest"]); Assert.Equal(before, f.Snapshot()); }
    }

    [Theory]
    [InlineData("missing")] [InlineData("ledger")] [InlineData("member")]
    public async Task TransferDeleteReturnsSameNotFoundBoundaryWithoutExposingOtherHouseholds(string mode)
    {
        await using var f = await RecordApiFixture.Create(); var path = await CreateTransfer(f, TransferInput(f)); var input = await TransferDeleteInput(f, path);
        if (mode == "missing") path = TransferRoot(f) + "/" + Guid.NewGuid();
        if (mode == "ledger")
        {
            using var created = await f.Client.PostAsJsonAsync("/ledgers", new { requestId = Guid.NewGuid(), name = "別家計簿" }); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var other = (await created.Content.ReadFromJsonAsync<JsonObject>())!["ledgerId"]!.GetValue<string>(); path = $"/ledgers/{D(other)}/transfers/{path.Split('/').Last()}";
        }
        if (mode == "member") f.App.Database.Execute("DELETE FROM ledger_members"); var before = f.Snapshot();
        var error = await TransferDeleteError(f, path, input, 404); Assert.Equal("not_found", error["code"]!.GetValue<string>()); Assert.Null(error["latest"]); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("csrf", 400)] [InlineData("origin", 403)] [InlineData("expired", 401)] [InlineData("anonymous", 401)]
    public async Task TransferDeleteMaintainsAuthenticationCsrfAndOrigin(string kind, int status)
    {
        await using var f = await RecordApiFixture.Create(); var path = await CreateTransfer(f, TransferInput(f)); var input = await TransferDeleteInput(f, path); using var anonymous = f.App.Client();
        if (kind == "csrf") f.Client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        if (kind == "origin") { f.Client.DefaultRequestHeaders.Remove("Origin"); f.Client.DefaultRequestHeaders.Add("Origin", "https://other.example.test"); }
        if (kind == "expired") f.App.Time.Advance(TimeSpan.FromDays(15)); var before = f.Snapshot();
        using var response = await SendTransferDelete(kind == "anonymous" ? anonymous : f.Client, path, input); Assert.Equal(status, (int)response.StatusCode); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferDeleteAndReplayUseSharedTwentyWriteLimit()
    {
        await using var f = await RecordApiFixture.Create(); var path = await CreateTransfer(f, TransferInput(f)); var input = await TransferDeleteInput(f, path);
        for (var i = 0; i < 12; i++) await DeleteTransfer(f, path, input); // 初期マスタ7 + POST1 + DELETE/再送12。
        var before = f.Snapshot(); using var response = await SendTransferDelete(f.Client, path, input); Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        using var shared = await f.Client.PostAsJsonAsync($"/ledgers/{D(f.Ledger)}/people", new { requestId = Guid.NewGuid(), name = "共有枠" }); Assert.Equal(HttpStatusCode.TooManyRequests, shared.StatusCode); Assert.Equal(before, f.Snapshot());
    }
}
