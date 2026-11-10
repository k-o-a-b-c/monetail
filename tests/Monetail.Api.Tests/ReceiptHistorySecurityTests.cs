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

namespace Monetail.Api.Tests;

public sealed partial class ReceiptApiTests
{
    [Theory]
    [InlineData("Admin", "person", "payment", 200)] [InlineData("Editor", "unmap", "payment", 200)]
    [InlineData("Admin", "Editor", "payment", 200)] [InlineData("Editor", "Admin", "payment", 200)] [InlineData("Editor", "Viewer", "payment", 200)]
    [InlineData("Viewer", "Editor", "tax", 200)] [InlineData("unmapped", "map", "tax", 200)]
    [InlineData("Editor", "person", "tax", 200)] [InlineData("Editor", "Viewer", "tax", 200)]
    [InlineData("Editor", "member", "payment", 404)] [InlineData("Editor", "expire", "payment", 401)] [InlineData("Editor", "revoke", "tax", 401)]
    public async Task HistoryAutofillClearsPaymentAfterMappingOrRoleChangeAndPreservesTax(string role, string change, string phase, int status)
    {
        await using var f = await Fixture.Create(); f.Role(role == "unmapped" ? "Editor" : role, role == "unmapped" ? null : f.Owner);
        HistoryReceipt(f, 1, "店舗"); HistoryReceipt(f, 2, "店舗", f.OtherPayment, inclusion: 1, rounding: "half_up");
        var interception = new HistoryReadMutation(f.App.Time, change, phase, f.Other, f.Owner);
        using var host = HistoryHost(f, interception); using var client = f.App.Client(host); await f.App.LoginAsync(client); interception.Armed = true;
        var before = f.Snapshot(); var result = await HistoryGet(f, "autofill", AutofillQuery(), status, client); Assert.False(interception.Armed);
        Assert.Equal(role is "Viewer" or "unmapped" ? 0 : 1, interception.PaymentQueries);
        if (status == 200)
        {
            Keys(result, "paymentMethodId", "taxInclusionMode", "taxRoundingMode"); Assert.Null(result["paymentMethodId"]);
            Assert.Equal(1, result["taxInclusionMode"]!.GetValue<int>()); Assert.Equal("half_up", result["taxRoundingMode"]!.GetValue<string>());
        }
        else { Assert.False(result.ContainsKey("paymentMethodId")); Assert.False(result.ContainsKey("taxInclusionMode")); Assert.Equal(before, f.Snapshot()); }
    }

    [Theory]
    [InlineData("member", 404)] [InlineData("expire", 401)] [InlineData("Viewer", 200)]
    public async Task HistorySuggestionsRecheckAccessAfterQuery(string change, int status)
    {
        await using var f = await Fixture.Create(); HistoryReceipt(f, 1, "店舗");
        var interception = new HistoryReadMutation(f.App.Time, change, "suggestions", f.Other, f.Owner);
        using var host = HistoryHost(f, interception); using var client = f.App.Client(host); await f.App.LoginAsync(client); interception.Armed = true;
        var result = await HistoryGet(f, "suggestions", "kind=shopName", status, client); Assert.False(interception.Armed);
        if (status == 200) Assert.Equal(new[] { "店舗" }, Names(result)); else Assert.False(result.ContainsKey("suggestions"));
    }

    [Theory]
    [InlineData("suggestions")] [InlineData("autofill")]
    public async Task HistoryUsesCommonStoreFailureAndDoesNotWrite(string operation)
    {
        await using var f = await Fixture.Create(); f.Role("Editor", f.Owner); HistoryReceipt(f, 1, "店舗");
        var interception = new HistoryReadMutation(f.App.Time, "store", operation == "suggestions" ? "suggestions" : "tax", f.Other, f.Owner);
        using var host = HistoryHost(f, interception); using var client = f.App.Client(host); await f.App.LoginAsync(client); interception.Armed = true;
        var before = f.Snapshot(); var error = await HistoryGet(f, operation, operation == "suggestions" ? "kind=shopName" : AutofillQuery(), 503, client);
        Assert.Equal("store_unavailable", error["code"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("Admin")] [InlineData("Editor")] [InlineData("Viewer")]
    public async Task HistoryAllMembersCanReadCandidatesWithoutAnOwnerMapping(string role)
    {
        await using var f = await Fixture.Create(); f.Role(role); HistoryReceipt(f, 1, "店舗");
        Assert.Equal(new[] { "店舗" }, Names(await HistoryGet(f, "suggestions", "kind=shopName")));
    }

    [Fact]
    public async Task HistoryAnonymousExpiredAndNonmemberCannotReadEitherEndpoint()
    {
        await using var f = await Fixture.Create(); HistoryReceipt(f, 1, "店舗"); using var anonymous = f.App.Client();
        foreach (var suffix in new[] { "receipt-suggestions?kind=shopName", "receipt-autofill?" + AutofillQuery() })
        { using var denied = await anonymous.GetAsync($"/ledgers/{D(f.Ledger)}/" + suffix); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode); }
        f.App.Database.Execute("DELETE FROM ledger_members");
        foreach (var (operation, query) in new[] { ("suggestions", "kind=shopName"), ("autofill", AutofillQuery()) })
        { var result = await HistoryGet(f, operation, query, 404); Assert.False(result.ContainsKey("suggestions")); Assert.False(result.ContainsKey("taxInclusionMode")); }
        f.App.Time.Advance(TimeSpan.FromDays(15)); using var expired = await f.Client.GetAsync($"/ledgers/{D(f.Ledger)}/receipt-autofill?" + AutofillQuery()); Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    [Fact]
    public async Task HistorySharesReadQuotaAcrossSuggestionsAutofillAndList()
    {
        await using var f = await Fixture.Create();
        for (var i = 0; i < 60; i++) { await HistoryGet(f, "suggestions", "kind=shopName"); await HistoryGet(f, "autofill", AutofillQuery()); }
        using var response = await f.Client.GetAsync(f.Root + "?month=all"); Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    private static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> HistoryHost(Fixture f, DbCommandInterceptor interceptor) =>
        f.App.NewFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddDbContext<ApiDbContext>(options => options.AddInterceptors(interceptor))));

    private sealed class HistoryReadMutation(LoginTestApp.LoginTime clock, string change, string phase, string other, string owner) : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public int PaymentQueries { get; private set; }
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            var payment = command.CommandText.Contains("AS PaymentMethodId FROM ledger_receipts r", StringComparison.Ordinal);
            if (payment) PaymentQueries++;
            var matches = phase switch
            {
                "payment" => payment,
                "tax" => command.CommandText.Contains("AS TaxInclusionMode", StringComparison.Ordinal),
                _ => command.CommandText.Contains("AS Value FROM ledger_receipts r", StringComparison.Ordinal)
            };
            if (!Armed || !matches) return result;
            Armed = false;
            if (change == "store") throw new InvalidOperationException("synthetic-history-read-failure");
            if (change == "expire") clock.Advance(TimeSpan.FromDays(15));
            else
            {
                using var mutation = command.Connection!.CreateCommand(); mutation.Transaction = command.Transaction;
                mutation.CommandText = change switch
                {
                    "member" => "DELETE FROM ledger_members",
                    "revoke" => "UPDATE auth_sessions SET RevokedAtUtc='2026-11-10 12:00:00'",
                    "person" => $"UPDATE ledger_members SET person_id='{other}'",
                    "unmap" => "UPDATE ledger_members SET person_id=NULL",
                    "map" => $"UPDATE ledger_members SET person_id='{owner}'",
                    _ => $"UPDATE ledger_members SET role='{change}'"
                };
                await mutation.ExecuteNonQueryAsync(cancellationToken);
            }
            return result;
        }
    }
}
