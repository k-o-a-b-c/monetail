using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Features.Monetail.Transfers;
using Monetail.Api.Persistence;
using Xunit;

namespace Monetail.Api.Tests;

// 既存の実HTTP・Identity・家計簿・隔離SQLite fixtureを共用する。
public sealed partial class ReceiptApiTests
{
    [Fact]
    public async Task TransferDetailReturnsOnlyEightFieldsAndDoesNotChangeAnyData()
    {
        await using var f = await Fixture.Create(); var row = await SeedTransferDetail(f); var before = f.Snapshot();
        var detail = await ReadTransfer(f.Client, TransferPath(f, row.Id));
        Keys(detail, "transferId", "transactionAt", "name", "fromPaymentMethodId", "toPaymentMethodId", "amount", "memo", "revision");
        Assert.Equal("2026-11-10T12:34:56", detail["transactionAt"]!.GetValue<string>());
        Assert.Equal("  銀行 → 現金  ", detail["name"]!.GetValue<string>()); Assert.Equal(int.MaxValue, detail["amount"]!.GetValue<int>()); Assert.Null(detail["memo"]);
        Assert.Equal(D(row.Id), detail["transferId"]!.GetValue<string>()); AssertD(detail["transferId"]!, '7');
        Assert.Equal(D(row.Revision), detail["revision"]!.GetValue<string>()); AssertD(detail["revision"]!, '4');
        Assert.Equal(D(row.FromPaymentMethodId), detail["fromPaymentMethodId"]!.GetValue<string>());
        Assert.Equal(D(row.ToPaymentMethodId), detail["toPaymentMethodId"]!.GetValue<string>());
        Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("Admin", false)] [InlineData("Editor", false)] [InlineData("Editor", true)] [InlineData("Viewer", false)]
    public async Task TransferDetailAllLedgerRolesCanReadOtherOwnersIncludingWithoutPersonMapping(string role, bool mapped)
    {
        await using var f = await Fixture.Create(); var row = await SeedTransferDetail(f); f.Role(role, mapped ? f.Owner : null);
        var before = f.Snapshot(); await ReadTransfer(f.Client, TransferPath(f, row.Id)); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("  元のメモ  ")]
    public async Task TransferDetailMemoNormalizesOnlyEmptyStringWithoutUpdatingDatabase(string? memo)
    {
        await using var f = await Fixture.Create(); var row = await SeedTransferDetail(f);
        using (var connection = f.App.Database.OpenForeignKeyConnection())
        {
            using var command = connection.CreateCommand(); command.CommandText = "PRAGMA ignore_check_constraints=ON"; command.ExecuteNonQuery();
            try
            {
                command.CommandText = "UPDATE ledger_transfers SET memo=$memo"; command.Parameters.AddWithValue("$memo", (object?)memo ?? DBNull.Value); command.ExecuteNonQuery();
            }
            finally { command.Parameters.Clear(); command.CommandText = "PRAGMA ignore_check_constraints=OFF"; command.ExecuteNonQuery(); }
        }
        var before = f.Snapshot(); var result = await ReadTransfer(f.Client, TransferPath(f, row.Id));
        Assert.Equal(string.IsNullOrEmpty(memo) ? null : memo, result["memo"]?.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("ledgerId", "not-a-uuid")] [InlineData("transferId", "not-a-uuid")]
    [InlineData("ledgerId", "00000000-0000-0000-0000-000000000000")] [InlineData("transferId", "00000000-0000-0000-0000-000000000000")]
    [InlineData("ledgerId", "00112233445566778899aabbccddeeff")] [InlineData("transferId", "00112233445566778899aabbccddeeff")]
    public async Task TransferDetailInvalidIdsUseCommonInputErrorAndDoNotChangeData(string field, string bad)
    {
        await using var f = await Fixture.Create(); var row = await SeedTransferDetail(f); var before = f.Snapshot();
        var path = $"/ledgers/{(field == "ledgerId" ? bad : D(f.Ledger))}/transfers/{(field == "transferId" ? bad : D(row.Id))}";
        var error = await ReadTransfer(f.Client, path, 400); Keys(error, "code", "message", "field", "latest", "permissions");
        Assert.Equal("invalid_input", error["code"]!.GetValue<string>()); Assert.Equal(field, error["field"]!.GetValue<string>());
        Assert.Null(error["latest"]); Assert.False(error.ContainsKey("transferId")); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("missing-transfer")] [InlineData("missing-ledger")] [InlineData("not-member")]
    public async Task TransferDetailNotFoundDoesNotExposeTransferOrAuditData(string kind)
    {
        await using var f = await Fixture.Create(); var row = await SeedTransferDetail(f); f.Role("Editor", f.Owner);
        var path = kind == "missing-ledger" ? $"/ledgers/{Guid.NewGuid():D}/transfers/{D(row.Id)}" : TransferPath(f, kind == "missing-transfer" ? Guid.NewGuid().ToString("N") : row.Id);
        if (kind == "not-member") f.App.Database.Execute("DELETE FROM ledger_members");
        var before = f.Snapshot(); var error = await ReadTransfer(f.Client, path, 404);
        Keys(error, "code", "message", "field", "latest", "permissions"); Assert.Equal("not_found", error["code"]!.GetValue<string>());
        Assert.Null(error["latest"]); Assert.DoesNotContain(row.Name, error.ToJsonString());
        if (kind == "missing-transfer") Assert.Equal(D(f.Owner), error["permissions"]!["myPersonId"]!.GetValue<string>());
        else { Assert.Null(error["permissions"]!["myPersonId"]); Assert.Null(error["permissions"]!["currentRole"]); }
        Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferDetailCompositeIdAndMembershipSeparateLedgers()
    {
        await using var f = await Fixture.Create(); var row = await SeedTransferDetail(f);
        using var create = await f.Client.PostAsJsonAsync("/ledgers", new { requestId = Guid.NewGuid(), name = "別家計簿" }); Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var other = (await create.Content.ReadFromJsonAsync<JsonObject>())!["ledgerId"]!.GetValue<string>(); var otherN = Guid.Parse(other).ToString("N");
        f.App.Database.Execute($"INSERT INTO ledger_people(ledger_id,person_id,name,display_order,revision,payment_order_revision) SELECT '{otherN}',person_id,name,display_order,revision,payment_order_revision FROM ledger_people WHERE ledger_id='{f.Ledger}'");
        f.App.Database.Execute($"INSERT INTO ledger_payment_methods(ledger_id,payment_method_id,owner_person_id,name,display_order,revision) SELECT '{otherN}',payment_method_id,owner_person_id,name,display_order,revision FROM ledger_payment_methods WHERE ledger_id='{f.Ledger}'");
        await SeedTransferDetail(f, otherN, row.Id, "別家計簿だけの名称");
        var before = f.Snapshot(); Assert.Equal(row.Name, (await ReadTransfer(f.Client, TransferPath(f, row.Id)))["name"]!.GetValue<string>());
        var otherPath = $"/ledgers/{D(otherN)}/transfers/{D(row.Id)}";
        Assert.Equal("別家計簿だけの名称", (await ReadTransfer(f.Client, otherPath))["name"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
        f.App.Database.Execute($"DELETE FROM ledger_members WHERE ledger_id='{otherN}'"); before = f.Snapshot();
        var denied = await ReadTransfer(f.Client, otherPath, 404); var absent = await ReadTransfer(f.Client, $"/ledgers/{D(otherN)}/transfers/{Guid.NewGuid():D}", 404);
        Assert.Equal(denied.ToJsonString(), absent.ToJsonString()); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("anonymous")] [InlineData("expired")] [InlineData("revoked")]
    public async Task TransferDetailRequiresCurrentAuthenticationBeforeExposingData(string kind)
    {
        await using var f = await Fixture.Create(); var row = await SeedTransferDetail(f); using var anonymous = f.App.Client();
        if (kind == "expired") f.App.Time.Advance(TimeSpan.FromDays(15));
        if (kind == "revoked") f.App.Database.Execute("UPDATE auth_sessions SET RevokedAtUtc=CreatedAtUtc");
        var before = f.Snapshot(); using var response = await (kind == "anonymous" ? anonymous : f.Client).GetAsync(TransferPath(f, row.Id));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("transferId", body); Assert.DoesNotContain(row.Name, body); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("member", 404)] [InlineData("expire", 401)] [InlineData("store", 503)]
    public async Task TransferDetailRechecksAccessAfterReadAndDoesNotLeakOnFailure(string kind, int status)
    {
        await using var f = await Fixture.Create(); var row = await SeedTransferDetail(f); var mutation = new TransferReadMutation(f.App.Time, kind);
        using var host = f.App.NewFactory().WithWebHostBuilder(b => b.ConfigureServices(services => services.AddDbContext<ApiDbContext>(options => options.AddInterceptors(mutation))));
        using var client = f.App.Client(host); await f.App.LoginAsync(client); await LoginTestApp.CsrfAsync(client);
        var before = f.Snapshot(); mutation.Armed = true; var error = await ReadTransfer(client, TransferPath(f, row.Id), status);
        Assert.False(mutation.Armed); Assert.False(error.ContainsKey("transferId")); Assert.DoesNotContain("synthetic-store-secret", error.ToJsonString()); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferDetailUsesExistingSharedReadLimitAndOriginBoundary()
    {
        await using var f = await Fixture.Create(); var row = await SeedTransferDetail(f);
        // GETはレシートと同じOrigin境界。CSRF必須の書込APIは今回追加しない。
        f.Client.DefaultRequestHeaders.Remove("Origin"); f.Client.DefaultRequestHeaders.Add("Origin", "https://other.example.test");
        for (var i = 0; i < 119; i++) { using var response = await f.Client.GetAsync($"/ledgers/{D(f.Ledger)}/people"); Assert.Equal(HttpStatusCode.OK, response.StatusCode); }
        var before = f.Snapshot(); await ReadTransfer(f.Client, TransferPath(f, row.Id));
        using var limited = await f.Client.GetAsync(TransferPath(f, row.Id)); Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        using var shared = await f.Client.GetAsync($"/ledgers/{D(f.Ledger)}/people"); Assert.Equal(HttpStatusCode.TooManyRequests, shared.StatusCode); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferDetailDoesNotAddPostEndpoint()
    {
        await using var f = await Fixture.Create(); var row = await SeedTransferDetail(f); var before = f.Snapshot();
        foreach (var method in new[] { HttpMethod.Post })
        {
            using var request = new HttpRequestMessage(method, TransferPath(f, row.Id)); using var response = await f.Client.SendAsync(request); Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }
        Assert.Equal(before, f.Snapshot());
    }

    private sealed class TransferReadMutation(LoginTestApp.LoginTime clock, string kind) : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken token = default)
        {
            if (Armed && command.CommandText.Contains("FROM \"ledger_transfers\"", StringComparison.Ordinal))
            {
                Armed = false;
                if (kind == "store") throw new InvalidOperationException("synthetic-store-secret");
                if (kind == "expire") clock.Advance(TimeSpan.FromDays(15));
                else { using var change = command.Connection!.CreateCommand(); change.Transaction = command.Transaction; change.CommandText = "DELETE FROM ledger_members"; await change.ExecuteNonQueryAsync(token); }
            }
            return result;
        }
    }

    private static string TransferPath(Fixture f, string id) => $"/ledgers/{D(f.Ledger)}/transfers/{D(id)}";
    private static async Task<JsonObject> ReadTransfer(HttpClient client, string path, int status = 200)
    {
        using var response = await client.GetAsync(path); var body = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == status, $"Expected {status}, got {(int)response.StatusCode}: {body}");
        return JsonNode.Parse(body)!.AsObject();
    }
    private static async Task<LedgerTransfer> SeedTransferDetail(Fixture f, string? ledger = null, string? id = null, string? name = null)
    {
        var row = new LedgerTransfer
        {
            LedgerId = ledger ?? Guid.Parse(f.Ledger).ToString("N"), Id = id ?? Guid.CreateVersion7().ToString("N"), Revision = Guid.NewGuid().ToString("N"),
            TransactionAt = new(2026, 11, 10, 12, 34, 56, DateTimeKind.Unspecified), Name = name ?? "  銀行 → 現金  ",
            FromPaymentMethodId = Guid.Parse(f.Payment).ToString("N"), ToPaymentMethodId = Guid.Parse(f.OtherPayment).ToString("N"), Amount = int.MaxValue,
            CreatedByAuthUserId = f.App.UserId, UpdatedByAuthUserId = f.App.UserId,
            CreatedAtUtc = new(2026, 11, 10, 3, 34, 56, DateTimeKind.Utc), UpdatedAtUtc = new(2026, 11, 10, 3, 34, 56, DateTimeKind.Utc)
        };
        await using var services = f.App.Database.CreateServices(); await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApiDbContext>(); context.Add(row); await context.SaveChangesAsync(); return row;
    }
}
