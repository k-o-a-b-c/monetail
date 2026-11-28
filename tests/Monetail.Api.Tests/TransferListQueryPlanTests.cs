using System.Data.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Persistence;
using Xunit;
using static Monetail.Api.Tests.RecordApiTestData;

namespace Monetail.Api.Tests;

public sealed partial class TransferListApiTests
{
    [Fact]
    public async Task TransferListActualSqlUsesExistingKeysetIndexWithoutOffsetCountOrTemporarySort()
    {
        await using var f = await RecordApiFixture.Create(); SeedTransferList(f, 1001); var capture = new TransferListCommandCapture();
        using var host = f.App.NewFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddDbContext<ApiDbContext>(options => options.AddInterceptors(capture))));
        using var client = f.App.Client(host); await f.App.LoginAsync(client); var before = f.Snapshot();
        var first = await TransferPage(f, "month=all", client: client);
        await TransferPage(f, "month=all&cursor=" + Uri.EscapeDataString(first["nextCursor"]!.GetValue<string>()), client: client);
        Assert.NotNull(capture.Sql); Assert.Contains("LIMIT 101", capture.Sql);
        Assert.DoesNotContain("OFFSET", capture.Sql, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("COUNT(", capture.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("(t.transaction_at, t.transfer_id COLLATE BINARY) < (@at, @id)", capture.Sql);
        using var connection = f.App.Database.OpenForeignKeyConnection(); using var plan = connection.CreateCommand();
        plan.CommandText = "EXPLAIN QUERY PLAN " + capture.Sql;
        foreach (var (name, value) in capture.Parameters) plan.Parameters.AddWithValue(name, value);
        var lines = new List<string>(); using (var reader = plan.ExecuteReader()) while (reader.Read()) lines.Add(reader.GetString(3));
        Assert.Contains(lines, line => line.Contains("SEARCH t USING INDEX ix_ledger_transfers_transaction", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("TEMP B-TREE", StringComparison.Ordinal)); Assert.Equal(before, f.Snapshot());
    }

}
