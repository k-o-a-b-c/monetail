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
    public async Task ListAllowsEveryCurrentMemberWithoutOwnerMapping(string role)
    {
        await using var f = await Fixture.Create(); SeedList(f, 1); f.Role(role);
        Assert.Single((await ListPage(f, "month=all"))["receipts"]!.AsArray());
    }

    [Fact]
    public async Task ListDoesNotExposeReceiptsToAnonymousExpiredOrRemovedMembers()
    {
        await using var f = await Fixture.Create(); SeedList(f, 101); var cursor = (await ListPage(f, "month=all"))["nextCursor"]!.GetValue<string>();
        using var anonymous = f.App.Client(); using var denied = await anonymous.GetAsync(f.Root + "?month=all"); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        // 全体Adminでも家計簿非参加なら404。カーソルの妥当性も先に公開しない。
        f.App.Database.Execute("DELETE FROM ledger_members");
        foreach (var value in new[] { cursor, "broken" })
        {
            var error = await ListPage(f, "month=all&cursor=" + Uri.EscapeDataString(value), 404); Assert.Null(error["latest"]); Assert.False(error.ContainsKey("receipts"));
        }
        f.App.Time.Advance(TimeSpan.FromDays(15));
        using var expired = await f.Client.GetAsync(f.Root + "?month=all"); Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    [Theory]
    [InlineData("member", 404)] [InlineData("expire", 401)] [InlineData("viewer", 200)]
    public async Task ListRechecksCurrentAccessAfterExecutingPageQuery(string kind, int status)
    {
        await using var f = await Fixture.Create(); SeedList(f, 101);
        var mutation = new ListReadMutation(f.App.Time, kind);
        using var host = f.App.NewFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddDbContext<ApiDbContext>(options => options.AddInterceptors(mutation))));
        using var client = f.App.Client(host); await f.App.LoginAsync(client); mutation.Armed = true; var before = f.Snapshot();
        var page = await ListPage(f, "month=all", status, client); Assert.False(mutation.Armed);
        if (status != 200) { Assert.False(page.ContainsKey("receipts")); Assert.Null(page["latest"]); Assert.Equal(before, f.Snapshot()); }
        else Assert.Equal(100, page["receipts"]!.AsArray().Count);
    }

    [Fact]
    public async Task ListSharesReadRateLimitWithExistingLedgerEndpoints()
    {
        await using var f = await Fixture.Create();
        for (var i = 0; i < 120; i++) await ListPage(f, "month=all");
        using var response = await f.Client.GetAsync($"/ledgers/{f.Ledger}/people"); Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task ListKeepsSameReceiptIdsAndForeignMasterFiltersIsolatedByLedger()
    {
        await using var f = await Fixture.Create(); SeedList(f, 1);
        using var create = await f.Client.PostAsJsonAsync("/ledgers", new { requestId = Guid.NewGuid(), name = "別家計簿" });
        var otherLedger = (await create.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>())!["ledgerId"]!.GetValue<string>();
        using var person = await f.Client.PostAsJsonAsync($"/ledgers/{otherLedger}/people", new { requestId = Guid.NewGuid(), name = "別の人物" });
        var otherPerson = (await person.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>())!["people"]![0]!["personId"]!.GetValue<string>();
        using var payment = await f.Client.PostAsJsonAsync($"/ledgers/{otherLedger}/payment-methods", new { requestId = Guid.NewGuid(), ownerPersonId = otherPerson, name = "別の支払方法" });
        var otherPayment = (await payment.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>())!["paymentMethodId"]!.GetValue<string>();
        var originalLedger = f.Ledger; f.Ledger = otherLedger; SeedList(f, 1, otherPayment, shop: "非公開の店"); f.Ledger = originalLedger;
        var page = await ListPage(f, "month=all"); Assert.Single(page["receipts"]!.AsArray()); Assert.Equal("店舗", page["receipts"]![0]!["shopName"]!.GetValue<string>());
        Assert.Empty((await ListPage(f, "month=all&ownerPersonId=" + D(otherPerson)))["receipts"]!.AsArray());
        Assert.Empty((await ListPage(f, "month=all&paymentMethodId=" + D(otherPayment)))["receipts"]!.AsArray());
    }

    private sealed class ListReadMutation(LoginTestApp.LoginTime clock, string kind) : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.Contains("FROM ledger_receipts r", StringComparison.Ordinal))
            {
                Armed = false;
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
