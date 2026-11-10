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
    [InlineData("member", false, 404)] [InlineData("expire", false, 401)]
    [InlineData("owner", true, 403)] [InlineData("member", true, 404)] [InlineData("expire", true, 401)]
    public async Task DetailAndConflictRecheckAccessAfterReadingLatest(string change, bool conflict, int expected)
    {
        await using var f = await Fixture.Create(); var id = await f.CreateReceipt(f.Input()); var input = f.Update(await f.Get(id));
        input["revision"] = Guid.NewGuid().ToString("D"); f.Role("Editor", f.Owner);
        var mutation = new ReceiptReadMutation(f.App.Time, change, f.Other);
        using var host = f.App.NewFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddDbContext<ApiDbContext>(options => options.AddInterceptors(mutation))));
        using var client = f.App.Client(host); await f.App.LoginAsync(client); await LoginTestApp.CsrfAsync(client);
        mutation.Armed = true; var before = f.Snapshot();
        using var response = conflict ? await client.PutAsJsonAsync(f.Root + "/" + id, input) : await client.GetAsync(f.Root + "/" + id);
        Assert.Equal(expected, (int)response.StatusCode); Assert.False(mutation.Armed);
        var text = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("\"receiptId\"", text);
        Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task NoChangePutStillRollsBackOperationRecordAfterLosingOwnerMapping()
    {
        await using var f = await Fixture.Create(); var id = await f.CreateReceipt(f.Input()); var input = f.Update(await f.Get(id)); f.Role("Editor", f.Owner);
        f.App.Database.Execute($"CREATE TRIGGER revoke_noop AFTER INSERT ON operation_requests WHEN NEW.operation_type='receipt.update' BEGIN UPDATE ledger_members SET person_id='{f.Other}'; END");
        var before = f.Snapshot(); await f.Call("PUT", input, 403, "/" + id); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task CommonReadAndWriteLimitsIncludeReceiptEndpoints()
    {
        await using var f = await Fixture.Create(); var id = await f.CreateReceipt(f.Input());
        for (var i = 0; i < 120; i++) await f.Get(id);
        using var read = await f.Client.GetAsync($"/ledgers/{f.Ledger}/people"); Assert.Equal(HttpStatusCode.TooManyRequests, read.StatusCode);
        var created = 0;
        for (var i = 0; i < 14; i++)
        {
            using var write = await f.Client.PostAsJsonAsync(f.Root, f.Input());
            if (write.StatusCode == HttpStatusCode.TooManyRequests) break;
            Assert.Equal(HttpStatusCode.Created, write.StatusCode); created++;
        }
        Assert.Equal(12, created); // 初期マスタ7操作＋最初のレシート1操作と同じ20回枠。
    }

    private sealed class ReceiptReadMutation(LoginTestApp.LoginTime clock, string kind, string other) : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.Contains("FROM \"ledger_categories\"", StringComparison.Ordinal))
            {
                Armed = false;
                if (kind == "expire") clock.Advance(TimeSpan.FromDays(15));
                else
                {
                    using var mutation = command.Connection!.CreateCommand(); mutation.Transaction = command.Transaction;
                    mutation.CommandText = kind == "member" ? "DELETE FROM ledger_members" : $"UPDATE ledger_members SET person_id='{other}'";
                    await mutation.ExecuteNonQueryAsync(cancellationToken);
                }
            }
            return result;
        }
    }
}
