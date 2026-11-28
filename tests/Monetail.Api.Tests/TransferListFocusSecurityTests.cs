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
    [Theory]
    [InlineData("Admin")] [InlineData("Editor")] [InlineData("Viewer")]
    public async Task TransferFocusAllowsAllMemberRolesWithoutPersonMapping(string role)
    {
        await using var f = await RecordApiFixture.Create(); var id = SeedTransferList(f, 1)[0]; f.Role(role); var before = f.Snapshot();
        var page = await TransferPage(f, "month=all&focusTransferId=" + D(id));
        Assert.Equal(D(id), Assert.Single(page["transfers"]!.AsArray())!["transferId"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("member", 404)] [InlineData("expire", 401)] [InlineData("deleted", 404)] [InlineData("store", 503)]
    public async Task TransferFocusNeverLeaksRowsAfterTargetLookupWhenAccessOrTargetChanges(string kind, int status)
    {
        await using var f = await RecordApiFixture.Create(); var id = SeedTransferList(f, 3)[1]; var mutation = new FocusReadMutation(f.App.Time, kind, id);
        using var host = f.App.NewFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddDbContext<ApiDbContext>(options => options.AddInterceptors(mutation))));
        using var client = f.App.Client(host); await f.App.LoginAsync(client); var before = f.Snapshot(); mutation.Armed = true;
        var error = await TransferPage(f, "month=all&focusTransferId=" + D(id), status, client);
        Assert.False(mutation.Armed); Assert.False(error.ContainsKey("transfers")); Assert.Null(error["latest"]);
        Assert.DoesNotContain("synthetic-store-secret", error.ToJsonString()); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferFocusUsesInclusiveKeysetBoundaryAndExistingIndexWithoutOffset()
    {
        await using var f = await RecordApiFixture.Create(); var id = SeedTransferList(f, 1001)[500]; var capture = new TransferListCommandCapture();
        using var host = f.App.NewFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddDbContext<ApiDbContext>(options => options.AddInterceptors(capture))));
        using var client = f.App.Client(host); await f.App.LoginAsync(client); var before = f.Snapshot();
        await TransferPage(f, "month=all&focusTransferId=" + D(id), client: client);
        Assert.NotNull(capture.Sql); Assert.Contains("(t.transaction_at, t.transfer_id COLLATE BINARY) <= (@at, @id)", capture.Sql);
        Assert.DoesNotContain("OFFSET", capture.Sql, StringComparison.OrdinalIgnoreCase); Assert.Contains("LIMIT 101", capture.Sql);
        using var connection = f.App.Database.OpenForeignKeyConnection(); using var command = connection.CreateCommand(); command.CommandText = "EXPLAIN QUERY PLAN " + capture.Sql;
        foreach (var (name, value) in capture.Parameters) command.Parameters.AddWithValue(name, value);
        var plan = new List<string>(); using (var reader = command.ExecuteReader()) while (reader.Read()) plan.Add(reader.GetString(3));
        Assert.Contains(plan, line => line.Contains("SEARCH t USING INDEX ix_ledger_transfers_transaction", StringComparison.Ordinal));
        Assert.DoesNotContain(plan, line => line.Contains("TEMP B-TREE", StringComparison.Ordinal)); Assert.Equal(before, f.Snapshot());
    }

    private sealed class FocusReadMutation(LoginTestApp.LoginTime clock, string kind, string target) : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken token = default)
        {
            if (Armed && command.CommandText.Contains("FROM ledger_transfers t", StringComparison.Ordinal) && command.CommandText.Contains("LIMIT 101", StringComparison.Ordinal))
            {
                Armed = false;
                if (kind == "store") throw new InvalidOperationException("synthetic-store-secret");
                if (kind == "expire") clock.Advance(TimeSpan.FromDays(15));
                else
                {
                    using var change = command.Connection!.CreateCommand(); change.Transaction = command.Transaction;
                    change.CommandText = kind == "member" ? "DELETE FROM ledger_members" : "DELETE FROM ledger_transfers WHERE transfer_id=$target";
                    if (kind == "deleted")
                    {
                        var parameter = change.CreateParameter(); parameter.ParameterName = "$target"; parameter.Value = target; change.Parameters.Add(parameter);
                    }
                    await change.ExecuteNonQueryAsync(token);
                }
            }
            return result;
        }
    }
}
