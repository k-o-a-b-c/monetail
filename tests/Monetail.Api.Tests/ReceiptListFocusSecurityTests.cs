using System.Data.Common;
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
    [InlineData("Admin")]
    [InlineData("Editor")]
    [InlineData("Viewer")]
    public async Task ReceiptFocusAllowsAllMemberRolesWithoutPersonMapping(string role)
    {
        await using var f = await Fixture.Create();
        var id = SeedList(f, 1)[0];
        f.Role(role);
        var before = f.Snapshot();
        var page = await ListPage(f, "month=all&focusReceiptId=" + D(id));
        Assert.Equal(D(id), Assert.Single(page["receipts"]!.AsArray())!["receiptId"]!.GetValue<string>());
        Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("member", 404)]
    [InlineData("expire", 401)]
    [InlineData("deleted", 404)]
    [InlineData("store", 503)]
    public async Task ReceiptFocusNeverLeaksRowsAfterTargetLookupWhenAccessOrTargetChanges(string kind, int status)
    {
        await using var f = await Fixture.Create();
        var id = SeedList(f, 3)[1];
        var mutation = new ReceiptFocusReadMutation(f.App.Time, kind, id);
        using var host = f.App.NewFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddDbContext<ApiDbContext>(options => options.AddInterceptors(mutation))));
        using var client = f.App.Client(host);
        await f.App.LoginAsync(client);
        var before = f.Snapshot();
        mutation.Armed = true;
        var error = await ListPage(f, "month=all&focusReceiptId=" + D(id), status, client);
        Assert.False(mutation.Armed);
        Assert.False(error.ContainsKey("receipts"));
        Assert.Null(error["latest"]);
        Assert.DoesNotContain("synthetic-store-secret", error.ToJsonString());
        Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task ReceiptFocusUsesInclusiveKeysetBoundaryAndExistingIndexWithoutOffset()
    {
        await using var f = await Fixture.Create();
        var id = SeedList(f, 1001)[500];
        var capture = new ReceiptFocusCommandCapture();
        using var host = f.App.NewFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddDbContext<ApiDbContext>(options => options.AddInterceptors(capture))));
        using var client = f.App.Client(host);
        await f.App.LoginAsync(client);
        var before = f.Snapshot();
        await ListPage(f, "month=all&focusReceiptId=" + D(id), client: client);
        Assert.NotNull(capture.Sql);
        Assert.Contains("(r.transaction_at, r.receipt_id COLLATE BINARY) <= (@at, @id)", capture.Sql);
        Assert.DoesNotContain("OFFSET", capture.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT 101", capture.Sql);
        using var connection = f.App.Database.OpenForeignKeyConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + capture.Sql;
        foreach (var (name, value) in capture.Parameters)
            command.Parameters.AddWithValue(name, value);
        var plan = new List<string>();
        using (var reader = command.ExecuteReader())
            while (reader.Read())
                plan.Add(reader.GetString(3));
        Assert.Contains(plan, line => line.Contains("SEARCH r USING INDEX ix_ledger_receipts_transaction", StringComparison.Ordinal));
        Assert.DoesNotContain(plan, line => line.Contains("TEMP B-TREE", StringComparison.Ordinal));
        Assert.Equal(before, f.Snapshot());
    }

    private sealed class ReceiptFocusReadMutation(LoginTestApp.LoginTime clock, string kind, string target) : DbCommandInterceptor
    {
        public bool Armed
        {
            get; set;
        }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken token = default)
        {
            if (Armed && command.CommandText.Contains("FROM ledger_receipts r", StringComparison.Ordinal) && command.CommandText.Contains("LIMIT 101", StringComparison.Ordinal))
            {
                Armed = false;
                if (kind == "store")
                    throw new InvalidOperationException("synthetic-store-secret");
                if (kind == "expire")
                    clock.Advance(TimeSpan.FromDays(15));
                else
                {
                    using var change = command.Connection!.CreateCommand();
                    change.Transaction = command.Transaction;
                    change.CommandText = kind == "member" ? "DELETE FROM ledger_members" : "DELETE FROM ledger_receipts WHERE receipt_id=$target";
                    if (kind == "deleted")
                    {
                        var parameter = change.CreateParameter();
                        parameter.ParameterName = "$target";
                        parameter.Value = target;
                        change.Parameters.Add(parameter);
                    }
                    await change.ExecuteNonQueryAsync(token);
                }
            }
            return result;
        }
    }
    private sealed class ReceiptFocusCommandCapture : DbCommandInterceptor
    {
        public string? Sql
        {
            get; private set;
        }
        public (string Name, object Value)[] Parameters { get; private set; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM ledger_receipts r", StringComparison.Ordinal) && command.CommandText.Contains("LIMIT 101", StringComparison.Ordinal))
            {
                Sql = command.CommandText;
                Parameters = command.Parameters.Cast<DbParameter>().Select(p => (p.ParameterName, p.Value!)).ToArray();
            }
            return ValueTask.FromResult(result);
        }
    }
}
