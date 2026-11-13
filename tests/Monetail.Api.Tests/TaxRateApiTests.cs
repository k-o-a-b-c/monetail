using System.Data.Common;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Persistence;
using Xunit;

namespace Monetail.Api.Tests;

// 共通の家計簿/認証/隔離SQLite fixtureを再利用する。
public sealed partial class CategoryApiTests
{
    private static string TaxPath(Fixture f) => $"/ledgers/{f.Ledger}/tax-rates";
    private static void TaxRow(Fixture f, string code, string start, string? end, string? rate)
    {
        var to = end is null ? "NULL" : $"'{end}'"; var value = rate is null ? "NULL" : $"'{rate}'";
        f.App.Database.Execute($"INSERT INTO tax_rate_history (id,tax_category_code,valid_from,valid_to,rate,revision) VALUES ('{Guid.NewGuid():N}','{code}','{start}',{to},{value},'{Guid.NewGuid():N}')");
    }
    [Fact]
    public async Task TaxRatesEmptyDatabaseDoesNotSeedHistory()
    {
        await using var f = await Fixture.Create(); var before = f.Snapshot();
        var result = await Send(f.Client, "GET", TaxPath(f));
        Assert.Equal(new[] { "rates" }, result.EnumerateObject().Select(p => p.Name)); Assert.Empty(result.GetProperty("rates").EnumerateArray());
        Assert.Equal(before, f.Snapshot());
    }
    [Theory]
    [InlineData("Admin")] [InlineData("Editor")] [InlineData("Viewer")]
    public async Task TaxRatesAllPeriodsAndCodesPreserveNullZeroAndDecimals(string role)
    {
        await using var f = await Fixture.Create(); f.Role(role);
        TaxRow(f, "Standard", "0001-01-01", "1989-03-31", null);
        TaxRow(f, "Standard", "1989-04-01", "1997-03-31", "3");
        TaxRow(f, "Standard", "2099-04-01", null, "0");
        TaxRow(f, "ReducedFood", "2019-10-01", null, "0.1250000000000000000000000001");
        TaxRow(f, "ReducedNewspaper", "2019-10-01", null, "0.1250000000000000000000000001");
        TaxRow(f, "Exempt", "0001-01-01", null, null);
        var before = f.Snapshot(); var result = await Send(f.Client, "GET", TaxPath(f));
        var rows = result.GetProperty("rates").EnumerateArray().ToArray(); Assert.Equal(6, rows.Length);
        Assert.All(rows, row => Assert.Equal(new[] { "rate", "taxCategoryCode", "validFrom", "validTo" }, row.EnumerateObject().Select(p => p.Name).Order()));
        Assert.Equal(JsonValueKind.Null, rows.Single(r => r.GetProperty("taxCategoryCode").GetString() == "Exempt").GetProperty("rate").ValueKind);
        var standard = rows.Where(r => r.GetProperty("taxCategoryCode").GetString() == "Standard").OrderBy(r => r.GetProperty("validFrom").GetString()).ToArray();
        Assert.Equal("0001-01-01", standard[0].GetProperty("validFrom").GetString()); Assert.Equal(JsonValueKind.Null, standard[0].GetProperty("rate").ValueKind);
        Assert.Equal("1989-03-31", standard[0].GetProperty("validTo").GetString()); Assert.Equal(JsonValueKind.String, standard[1].GetProperty("rate").ValueKind); Assert.Equal(3m, decimal.Parse(standard[1].GetProperty("rate").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(0m, decimal.Parse(standard[2].GetProperty("rate").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        foreach (var code in new[] { "ReducedFood", "ReducedNewspaper" })
            Assert.Equal("0.1250000000000000000000000001", rows.Single(r => r.GetProperty("taxCategoryCode").GetString() == code).GetProperty("rate").GetString());
        Assert.Equal(before, f.Snapshot());
    }
    [Fact]
    public async Task TaxRatesSharedAcrossLedgersButMembershipIsRequired()
    {
        await using var f = await Fixture.Create(); TaxRow(f, "Standard", "2000-01-01", null, "10");
        var other = await Send(f.Client, "POST", "/ledgers", new { requestId = Guid.NewGuid(), name = "別家計簿" }, 201);
        var otherId = other.GetProperty("ledgerId").GetString();
        var a = await Send(f.Client, "GET", TaxPath(f)); var b = await Send(f.Client, "GET", $"/ledgers/{otherId}/tax-rates");
        Assert.Equal(a.GetRawText(), b.GetRawText());
        f.App.Database.Execute($"DELETE FROM ledger_members WHERE ledger_id='{f.Ledger}'");
        var denied = await Send(f.Client, "GET", TaxPath(f), status: 404); Assert.False(denied.TryGetProperty("rates", out _));
        await Send(f.Client, "GET", $"/ledgers/{Guid.NewGuid():D}/tax-rates", status: 404);
        await Send(f.Client, "GET", $"/ledgers/{otherId}/tax-rates");
        using var anonymous = f.App.Client(); using var response = await anonymous.GetAsync(TaxPath(f)); Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
    [Fact]
    public async Task TaxRatesKeepsExistingReadAndWriteOriginBoundary()
    {
        await using var f = await Fixture.Create();
        f.Client.DefaultRequestHeaders.Remove("Origin"); f.Client.DefaultRequestHeaders.Add("Origin", "https://other.example.test");
        await Send(f.Client, "GET", TaxPath(f));
        await f.Call("POST", CreateInput(), 403);
    }
    [Fact]
    public async Task TaxRatesUsesSharedReadLimit()
    {
        await using var f = await Fixture.Create();
        for (var i = 0; i < 120; i++) await Send(f.Client, "GET", TaxPath(f));
        await Send(f.Client, "GET", $"/ledgers/{f.Ledger}/people", status: 429);
    }
    [Theory]
    [InlineData("member", 404)] [InlineData("expire", 401)] [InlineData("store", 503)]
    public async Task TaxRatesRevalidatesAccessAndDoesNotLeakOnReadFailure(string kind, int expected)
    {
        await using var f = await Fixture.Create(); TaxRow(f, "Standard", "2000-01-01", null, "10");
        var mutation = new TaxReadMutation(f.App.Time, kind);
        await using var factory = f.App.Factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
            services.AddDbContext<ApiDbContext>((_, options) => options.AddInterceptors(mutation))));
        using var client = f.App.Client(factory); client.DefaultRequestHeaders.Add("Cookie", f.App.SessionCookie);
        var before = f.Snapshot(); mutation.Armed = true;
        var result = await Send(client, "GET", TaxPath(f), status: expected);
        Assert.False(mutation.Armed); Assert.False(result.TryGetProperty("rates", out _)); Assert.Equal(before, f.Snapshot());
    }
    private sealed class TaxReadMutation(LoginTestApp.LoginTime clock, string kind) : DbCommandInterceptor
    {
        public bool Armed;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken token = default)
        {
            if (Armed && command.CommandText.Contains("FROM \"tax_rate_history\"", StringComparison.Ordinal))
            {
                Armed = false;
                if (kind == "store") throw new InvalidOperationException("synthetic");
                if (kind == "expire") clock.Advance(TimeSpan.FromDays(15));
                else { using var change = command.Connection!.CreateCommand(); change.Transaction = command.Transaction; change.CommandText = "DELETE FROM ledger_members"; await change.ExecuteNonQueryAsync(token); }
            }
            return result;
        }
    }
}
