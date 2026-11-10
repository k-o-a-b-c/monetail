using System.Data.Common;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Monetail.Api.Tests;

public sealed partial class ReceiptApiTests
{
    [Fact]
    public async Task HistoryMeasuresTenThousandReceiptsAndHundredThousandDetailsWithActualPlans()
    {
        await using var f = await Fixture.Create(); f.Role("Editor", f.Owner); SeedList(f, 10000);
        f.App.Database.Execute($"""
            UPDATE ledger_receipts SET shop_name='店舗'||printf('%03d',rowid%100),shop_location='所在地'||printf('%03d',rowid%200),
              updated_at_utc=strftime('%Y-%m-%d %H:%M:%S','2026-01-01','+'||(rowid%3600)||' seconds');
            INSERT INTO ledger_receipt_groups(ledger_id,group_id,receipt_id,category_id,memo)
              SELECT ledger_id,printf('%032x',1000000+rowid*2),receipt_id,'{f.Category}',NULL FROM ledger_receipts
              UNION ALL SELECT ledger_id,printf('%032x',1000001+rowid*2),receipt_id,'{f.OtherCategory}',NULL FROM ledger_receipts;
            WITH RECURSIVE numbers(n) AS(SELECT 1 UNION ALL SELECT n+1 FROM numbers WHERE n<5)
            INSERT INTO ledger_receipt_details(ledger_id,detail_id,group_id,display_order,name,unit_price,quantity,unit_discount_amount,discount_rate,tax_rate,expense_type,occurrence_type,memo)
              SELECT g.ledger_id,printf('%032x',2000000+g.rowid*5+n),g.group_id,n-1,'品名'||printf('%03d',(g.rowid+n)%150),100,1,0,'0',NULL,'Variable','Irregular',NULL
              FROM ledger_receipt_groups g CROSS JOIN numbers;
            """);
        Assert.Equal(10000L, f.App.Database.Scalar("SELECT COUNT(*) FROM ledger_receipts")); Assert.Equal(100000L, f.App.Database.Scalar("SELECT COUNT(*) FROM ledger_receipt_details"));
        var capture = new HistoryPlanCapture(); using var host = HistoryHost(f, capture); using var client = f.App.Client(host); await f.App.LoginAsync(client);
        var samples = new List<(string Label, string Operation, string Query)>
        {
            ("shops-empty","suggestions","kind=shopName"), ("shops-search","suggestions","kind=shopName&q=" + Uri.EscapeDataString("てんぽ")),
            ("locations-empty","suggestions","kind=shopLocation&shopName=" + Uri.EscapeDataString("店舗001")),
            ("locations-search","suggestions","kind=shopLocation&shopName=" + Uri.EscapeDataString("店舗001") + "&q=" + Uri.EscapeDataString("所在地")),
            ("items-empty","suggestions","kind=itemName&shopName=" + Uri.EscapeDataString("店舗001") + "&categoryId=" + D(f.Category)),
            ("items-no-match","suggestions","kind=itemName&shopName=" + Uri.EscapeDataString("店舗001") + "&categoryId=" + D(f.Category) + "&q=" + new string('a', 100)),
            ("autofill","autofill","shopName=" + Uri.EscapeDataString("店舗001") + "&transactionDate=2026-11-10")
        };
        Console.WriteLine("Receipt history performance: 10000 receipts / 100000 details, 5 real HTTP samples per condition.");
        foreach (var sample in samples)
        {
            var times = new List<double>(); capture.Commands.Clear();
            for (var i = 0; i < 5; i++)
            {
                var timer = Stopwatch.StartNew(); var result = await HistoryGet(f, sample.Operation, sample.Query, client: client); timer.Stop(); times.Add(timer.Elapsed.TotalMilliseconds);
                if (sample.Operation == "suggestions") Assert.InRange(Names(result).Length, 0, 100);
            }
            times.Sort(); Console.WriteLine($"{sample.Label}: median={times[2]:F1} ms; sample-p95={times[4]:F1} ms (n=5)");
            Assert.NotEmpty(capture.Commands);
            using var connection = f.App.Database.OpenForeignKeyConnection(); connection.CreateFunction<string?, string, bool>("monetail_history_contains", (value, _) => value is not null, isDeterministic: true);
            foreach (var captured in capture.Commands.DistinctBy(item => item.Sql))
            {
                using var plan = connection.CreateCommand(); plan.CommandText = "EXPLAIN QUERY PLAN " + captured.Sql;
                foreach (var (key, value) in captured.Parameters) plan.Parameters.AddWithValue(key, value);
                var details = new List<string>(); using (var reader = plan.ExecuteReader()) while (reader.Read()) details.Add(reader.GetString(3));
                Assert.DoesNotContain("OFFSET", captured.Sql, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(details, detail => detail.Contains("SEARCH", StringComparison.Ordinal) && detail.Contains("ledger_id", StringComparison.Ordinal));
                if (sample.Label.StartsWith("items-", StringComparison.Ordinal))
                    Assert.Contains(details, detail => detail.Contains("SEARCH d", StringComparison.Ordinal) && detail.Contains("group_id=?", StringComparison.Ordinal));
                Console.WriteLine(sample.Label + " plan: " + string.Join(" / ", details));
            }
        }
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => HistoryGet(f, "suggestions", samples[4].Query, client: client)));
        Assert.All(results, result => Assert.Equal(Names(results[0]), Names(result)));
        Assert.Equal(100000L, f.App.Database.Scalar("SELECT COUNT(*) FROM ledger_receipt_details"));
    }

    private sealed class HistoryPlanCapture : DbCommandInterceptor
    {
        public List<(string Sql, (string Key, object Value)[] Parameters)> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM ledger_receipts r", StringComparison.Ordinal))
                lock (Commands) Commands.Add((command.CommandText, command.Parameters.Cast<DbParameter>().Select(p => (p.ParameterName, p.Value!)).ToArray()));
            return ValueTask.FromResult(result);
        }
    }
}
