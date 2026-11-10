using System.Data.Common;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Persistence;
using Xunit;

namespace Monetail.Api.Tests;

public sealed partial class ReceiptApiTests
{
    [Fact]
    public async Task ListLargeDatasetUsesKeysetIndexAndMeasuresSelectiveAndNormalizedSearch()
    {
        await using var f = await Fixture.Create(); SeedList(f, 90000); SeedList(f, 10000, f.OtherPayment, start: 90000);
        var capture = new ListCommandCapture();
        using var host = f.App.NewFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddDbContext<ApiDbContext>(options => options.AddInterceptors(capture))));
        using var client = f.App.Client(host); await f.App.LoginAsync(client);
        var results = new List<string>();
        async Task<JsonObject> Measure(string label, string query, int count, bool orderedIndex)
        {
            var timer = Stopwatch.StartNew(); var page = await ListPage(f, query, client: client); timer.Stop();
            Assert.Equal(count, page["receipts"]!.AsArray().Count);
            Assert.NotNull(capture.Sql); Assert.Contains("LIMIT 101", capture.Sql); Assert.DoesNotContain("OFFSET", capture.Sql, StringComparison.OrdinalIgnoreCase);
            using var connection = f.App.Database.OpenForeignKeyConnection();
            connection.CreateFunction<string, string, bool>("monetail_shop_contains", (_, _) => true, isDeterministic: true);
            using var plan = connection.CreateCommand(); plan.CommandText = "EXPLAIN QUERY PLAN " + capture.Sql;
            foreach (var (name, value) in capture.Parameters) plan.Parameters.AddWithValue(name, value);
            var lines = new List<string>(); using (var reader = plan.ExecuteReader()) while (reader.Read()) lines.Add(reader.GetString(3));
            if (orderedIndex)
            {
                Assert.Contains(lines, line => line.Contains("SEARCH r USING INDEX ix_ledger_receipts_transaction", StringComparison.Ordinal));
                Assert.DoesNotContain(lines, line => line.Contains("TEMP B-TREE", StringComparison.Ordinal));
            }
            results.Add($"{label}: {timer.Elapsed.TotalMilliseconds:F1} ms; {string.Join(" / ", lines)}");
            return page;
        }
        var first = await Measure("all-first", "month=all", 100, true);
        var provider = host.Services.GetRequiredService<IDataProtectionProvider>(); var protector = provider.CreateProtector("Monetail.ReceiptList.Cursor.v1");
        var position = JsonNode.Parse(protector.Unprotect(first["nextCursor"]!.GetValue<string>()))!.AsObject();
        position["ReceiptId"] = 201.ToString("x32"); // 99,800件をスキップした位置を、発行元で保護して再現する。
        await Measure("deep-equal-time", "month=all&cursor=" + Uri.EscapeDataString(protector.Protect(position.ToJsonString())), 100, true);
        await Measure("month", "month=2026-11", 100, true);
        await Measure("owner", "month=all&ownerPersonId=" + D(f.Owner), 100, true);
        await Measure("payment", "month=all&paymentMethodId=" + D(f.Payment), 100, false);
        await Measure("normalized-no-match", "month=all&q=" + Uri.EscapeDataString("存在しないすーぱー"), 0, true);
        var report = string.Join(Environment.NewLine, results);
        Console.WriteLine("Receipt list performance (100000 rows, real HTTP, isolated SQLite):" + Environment.NewLine + report);
        Assert.Equal(100000L, f.App.Database.Scalar("SELECT COUNT(*) FROM ledger_receipts"));
    }

    private sealed class ListCommandCapture : DbCommandInterceptor
    {
        public string? Sql { get; private set; }
        public (string Name, object Value)[] Parameters { get; private set; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM ledger_receipts r", StringComparison.Ordinal))
            {
                Sql = command.CommandText; Parameters = command.Parameters.Cast<DbParameter>().Select(p => (p.ParameterName, p.Value!)).ToArray();
            }
            return ValueTask.FromResult(result);
        }
    }
}
