using System.Net.Http.Json;
using System.Data.Common;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Persistence;
using Xunit;

namespace Monetail.Api.Tests;

public sealed partial class ReceiptApiTests
{
    [Theory]
    [InlineData("Admin")] [InlineData("Editor")] [InlineData("Viewer")]
    public async Task TransferListAllowsEveryCurrentMemberWithoutOwnerMapping(string role)
    {
        await using var f = await Fixture.Create(); SeedTransferList(f, 1); f.Role(role);
        Assert.Single((await TransferPage(f, "month=all"))["transfers"]!.AsArray());
    }

    [Fact]
    public async Task TransferListDoesNotExposeTransfersToAnonymousExpiredOrRemovedMembers()
    {
        await using var f = await Fixture.Create(); SeedTransferList(f, 101); var cursor = (await TransferPage(f, "month=all"))["nextCursor"]!.GetValue<string>();
        using var anonymous = f.App.Client(); using var denied = await anonymous.GetAsync(TransferRoot(f) + "?month=all"); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        // 全体Adminでも家計簿非参加なら404。カーソルの妥当性も先に公開しない。
        f.App.Database.Execute("DELETE FROM ledger_members");
        foreach (var value in new[] { cursor, "broken" })
        {
            var error = await TransferPage(f, "month=all&cursor=" + Uri.EscapeDataString(value), 404); Assert.Null(error["latest"]); Assert.False(error.ContainsKey("transfers"));
        }
        f.App.Time.Advance(TimeSpan.FromDays(15));
        using var expired = await f.Client.GetAsync(TransferRoot(f) + "?month=all"); Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    [Theory]
    [InlineData("member", 404)] [InlineData("expire", 401)] [InlineData("viewer", 200)] [InlineData("store", 503)]
    public async Task TransferListRechecksCurrentAccessAfterExecutingPageQuery(string kind, int status)
    {
        await using var f = await Fixture.Create(); SeedTransferList(f, 101);
        var mutation = new TransferListReadMutation(f.App.Time, kind);
        using var host = f.App.NewFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddDbContext<ApiDbContext>(options => options.AddInterceptors(mutation))));
        using var client = f.App.Client(host); await f.App.LoginAsync(client); mutation.Armed = true; var before = f.Snapshot();
        var page = await TransferPage(f, "month=all", status, client); Assert.False(mutation.Armed);
        if (status != 200) { Assert.DoesNotContain("synthetic-store-secret", page.ToJsonString()); Assert.False(page.ContainsKey("transfers")); Assert.Null(page["latest"]); Assert.Equal(before, f.Snapshot()); }
        else Assert.Equal(100, page["transfers"]!.AsArray().Count);
    }

    [Fact]
    public async Task TransferListSharesReadRateLimitWithExistingLedgerEndpoints()
    {
        await using var f = await Fixture.Create();
        for (var i = 0; i < 120; i++) await TransferPage(f, "month=all");
        using var response = await f.Client.GetAsync($"/ledgers/{f.Ledger}/people"); Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task TransferListSeparatesSameIdsAndForeignMasterFiltersByLedger()
    {
        await using var f = await Fixture.Create(); SeedTransferList(f, 1);
        using var create = await f.Client.PostAsJsonAsync("/ledgers", new { requestId = Guid.NewGuid(), name = "別家計簿" }); Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var otherLedger = (await create.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>())!["ledgerId"]!.GetValue<string>();
        using var person = await f.Client.PostAsJsonAsync($"/ledgers/{otherLedger}/people", new { requestId = Guid.NewGuid(), name = "別人物" }); Assert.Equal(HttpStatusCode.Created, person.StatusCode);
        var otherPerson = (await person.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>())!["people"]![0]!["personId"]!.GetValue<string>();
        var payments = new List<string>();
        foreach (var name in new[] { "別元", "別先" })
        {
            using var payment = await f.Client.PostAsJsonAsync($"/ledgers/{otherLedger}/payment-methods", new { requestId = Guid.NewGuid(), ownerPersonId = otherPerson, name }); Assert.Equal(HttpStatusCode.Created, payment.StatusCode);
            payments.Add((await payment.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>())!["paymentMethodId"]!.GetValue<string>());
        }
        // 支払方法IDも家計簿間で同じ値を許す。EXISTSの家計簿条件が欠けると、この別所有者が漏れる。
        f.App.Database.Execute($"INSERT INTO ledger_payment_methods(ledger_id,payment_method_id,owner_person_id,name,display_order,revision) VALUES('{otherLedger}','{f.Payment}','{otherPerson}','同一ID別所有',2,'{Guid.NewGuid():N}')");
        SeedTransferList(f, 1, payments[0], payments[1], name: "別家計簿の非公開名", ledger: otherLedger); var before = f.Snapshot();
        var page = await TransferPage(f, "month=all"); Assert.Equal("振替", Assert.Single(page["transfers"]!.AsArray())!["name"]!.GetValue<string>());
        Assert.Empty((await TransferPage(f, "month=all&ownerPersonId=" + D(otherPerson)))["transfers"]!.AsArray());
        Assert.Empty((await TransferPage(f, "month=all&paymentMethodId=" + D(payments[0])))["transfers"]!.AsArray());
        Assert.Equal(before, f.Snapshot());
    }

    private sealed class TransferListReadMutation(LoginTestApp.LoginTime clock, string kind) : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.Contains("FROM ledger_transfers t", StringComparison.Ordinal))
            {
                Armed = false;
                if (kind == "store") throw new InvalidOperationException("synthetic-store-secret");
                if (kind == "expire") clock.Advance(TimeSpan.FromDays(15));
                else
                {
                    using var mutation = command.Connection!.CreateCommand(); mutation.Transaction = command.Transaction;
                    mutation.CommandText = kind == "member" ? "DELETE FROM ledger_members" : "UPDATE ledger_members SET role='Viewer'";
                    await mutation.ExecuteNonQueryAsync(cancellationToken);
                }
            }
            return result;
        }
    }
}
