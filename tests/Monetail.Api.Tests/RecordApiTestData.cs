using System.Data.Common;
using System.Net;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Features.Monetail.Transfers;
using Monetail.Api.Persistence;
using Xunit;

namespace Monetail.Api.Tests;

internal static class RecordApiTestData
{
    internal static async Task<string> CreateTransfer(RecordApiFixture f, JsonObject input, HttpClient? client = null)
    {
        using var response = await (client ?? f.Client).PostAsJsonAsync(TransferRoot(f), input); var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, body); Assert.Empty(body);
        Assert.NotNull(response.Headers.Location); var location = response.Headers.Location.OriginalString;
        Assert.StartsWith($"/ledgers/{D(f.Ledger)}/transfers/", location); Assert.True(Guid.TryParseExact(location.Split('/').Last(), "D", out _)); return location;
    }


    internal static JsonObject TransferInput(RecordApiFixture f) => new()
    {
        ["requestId"] = Guid.NewGuid().ToString("D"), ["transactionAt"] = "2026-11-10T12:34:56", ["name"] = "振替",
        ["fromPaymentMethodId"] = D(f.Payment), ["toPaymentMethodId"] = D(f.OtherPayment), ["amount"] = 1, ["memo"] = null
    };


    internal static async Task<string> OwnTransferPayment(RecordApiFixture f)
    {
        using var response = await f.Client.PostAsJsonAsync($"/ledgers/{D(f.Ledger)}/payment-methods", new { requestId = Guid.NewGuid(), ownerPersonId = D(f.Owner), name = "本人の預金" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); return (await response.Content.ReadFromJsonAsync<JsonObject>())!["paymentMethodId"]!.GetValue<string>();
    }


    internal static async Task<LedgerTransfer> StoredTransfer(RecordApiFixture f)
    {
        await using var services = f.App.Database.CreateServices(); await using var scope = services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<ApiDbContext>().Set<LedgerTransfer>().AsNoTracking().SingleAsync();
    }

    internal static async Task<JsonObject> TransferUpdateInput(RecordApiFixture f, string location)
    {
        var input = await ReadTransfer(f.Client, location); input.Remove("transferId"); input["requestId"] = Guid.NewGuid().ToString("D"); return input;
    }

    internal static string TransferCandidate(RecordApiFixture f, int number, string name, string updated = "2026-11-10 12:00:00")
    {
        var id = SeedTransferList(f, 1, name: name, start: number - 1)[0];
        using var connection = f.App.Database.OpenForeignKeyConnection(); using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ledger_transfers SET updated_at_utc=@updated WHERE ledger_id=@ledger AND transfer_id=@id";
        command.Parameters.AddWithValue("@updated", updated); command.Parameters.AddWithValue("@ledger", f.Ledger); command.Parameters.AddWithValue("@id", id); command.ExecuteNonQuery(); return id;
    }

    internal static async Task<JsonObject> TransferSuggestions(RecordApiFixture f, string? query = null, int status = 200, HttpClient? client = null, string? ledger = null)
    {
        using var response = await (client ?? f.Client).GetAsync($"/ledgers/{D(ledger ?? f.Ledger)}/transfer-suggestions" + (query is null ? "" : "?" + query));
        var body = await response.Content.ReadAsStringAsync(); Assert.True((int)response.StatusCode == status, $"Expected {status}, got {(int)response.StatusCode}: {body}"); return JsonNode.Parse(body)!.AsObject();
    }

    internal static async Task<JsonObject> HistoryGet(RecordApiFixture f, string operation, string query, int status = 200, HttpClient? client = null)
    {
        using var response = await (client ?? f.Client).GetAsync($"/ledgers/{D(f.Ledger)}/receipt-{operation}?{query}");
        var text = await response.Content.ReadAsStringAsync(); Assert.True((int)response.StatusCode == status, $"Expected {status}, got {(int)response.StatusCode}: {text}");
        return JsonNode.Parse(text)!.AsObject();
    }

    internal static string[] Names(JsonObject result) => result["suggestions"]!.AsArray().Select(p => p!.GetValue<string>()).ToArray();

    internal sealed class TransferUpdateReadMutation(string sql) : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        private int reads;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken token = default)
        {
            if (Armed && command.CommandText.Contains("FROM \"ledger_transfers\"", StringComparison.Ordinal) && ++reads == 2)
            { Armed = false; using var mutate = command.Connection!.CreateCommand(); mutate.Transaction = command.Transaction; mutate.CommandText = sql; await mutate.ExecuteNonQueryAsync(token); }
            return result;
        }
    }

    internal sealed class TransferUpdateWriteMutation(string needle, string sql) : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (Armed && command.CommandText.Contains(needle, StringComparison.Ordinal)) { Armed = false; using var mutate = command.Connection!.CreateCommand(); mutate.Transaction = command.Transaction; mutate.CommandText = sql; await mutate.ExecuteNonQueryAsync(token); }
            return result;
        }
    }

    internal static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> HistoryHost(RecordApiFixture f, DbCommandInterceptor interceptor) =>
        f.App.NewFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddDbContext<ApiDbContext>(options => options.AddInterceptors(interceptor))));

    internal static void AssertD(JsonNode value, char? version = null)
    {
        var text = value.GetValue<string>(); Assert.True(Guid.TryParseExact(text, "D", out var id)); Assert.Equal(id.ToString("D"), text);
        if (version is char digit) Assert.Equal(digit, id.ToString("N")[12]);
    }


    internal static void Keys(JsonObject value, params string[] keys) => Assert.Equal(keys.Order(), value.Select(p => p.Key).Order());


    internal static string D(string value) => Guid.Parse(value).ToString("D");


    internal static async Task<LedgerTransfer> SeedTransferDetail(RecordApiFixture f, string? ledger = null, string? id = null, string? name = null)
    {
        var row = new LedgerTransfer
        {
            LedgerId = ledger ?? Guid.Parse(f.Ledger).ToString("N"), Id = id ?? Guid.CreateVersion7().ToString("N"), Revision = Guid.NewGuid().ToString("N"),
            TransactionAt = new(2026, 11, 10, 12, 34, 56, DateTimeKind.Unspecified), Name = name ?? "  銀行 → 現金  ",
            FromPaymentMethodId = Guid.Parse(f.Payment).ToString("N"), ToPaymentMethodId = Guid.Parse(f.OtherPayment).ToString("N"), Amount = int.MaxValue,
            CreatedByAuthUserId = f.App.UserId, UpdatedByAuthUserId = f.App.UserId,
            CreatedAtUtc = new(2026, 11, 10, 3, 34, 56, DateTimeKind.Utc), UpdatedAtUtc = new(2026, 11, 10, 3, 34, 56, DateTimeKind.Utc)
        };
        await using var services = f.App.Database.CreateServices(); await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApiDbContext>(); context.Add(row); await context.SaveChangesAsync(); return row;
    }

    internal static async Task<JsonObject> ReadTransfer(HttpClient client, string path, int status = 200)
    {
        using var response = await client.GetAsync(path); var body = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == status, $"Expected {status}, got {(int)response.StatusCode}: {body}");
        return JsonNode.Parse(body)!.AsObject();
    }

    internal static string[] SeedTransferList(RecordApiFixture f, int count, string? from = null, string? to = null,
        string at = "2026-11-10T12:34:56", string name = "振替", int start = 0, string? ledger = null)
    {
        if (count == 0) return [];
        using var connection = f.App.Database.OpenForeignKeyConnection(); using var command = connection.CreateCommand();
        command.CommandText = """
            WITH RECURSIVE numbers(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM numbers WHERE n < @count)
            INSERT INTO ledger_transfers(ledger_id,transfer_id,transaction_at,name,from_payment_method_id,to_payment_method_id,amount,memo,revision,
                created_by_auth_user_id,created_at_utc,updated_by_auth_user_id,updated_at_utc)
            SELECT @ledger,printf('%032x',n+@start),@at,@name,@from,@to,n,NULL,@revision,@user,'2026-11-10 00:00:00',@user,'2026-11-10 00:00:00' FROM numbers
            """;
        foreach (var (key, value) in new (string, object)[] { ("@count", count), ("@start", start), ("@ledger", ledger ?? f.Ledger), ("@at", at),
            ("@name", name), ("@from", from ?? f.Payment), ("@to", to ?? f.OtherPayment), ("@revision", Guid.NewGuid().ToString("N")), ("@user", f.App.UserId) }) command.Parameters.AddWithValue(key, value);
        command.ExecuteNonQuery(); return Enumerable.Range(start + 1, count).Select(n => n.ToString("x32", CultureInfo.InvariantCulture)).ToArray();
    }

    internal static Task<JsonObject> TransferPage(RecordApiFixture f, string query, int status = 200, HttpClient? client = null) =>
        ReadTransfer(client ?? f.Client, $"/ledgers/{D(f.Ledger)}/transfers?{query}", status);

    internal static string TransferRoot(RecordApiFixture f) => $"/ledgers/{D(f.Ledger)}/transfers";

    internal sealed class TransferOwnerMutation(string owner, string payment) : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken token = default)
        {
            if (Armed && command.CommandText.Contains("INSERT INTO \"operation_requests\"", StringComparison.Ordinal))
            {
                Armed = false; using var mutate = command.Connection!.CreateCommand(); mutate.Transaction = command.Transaction;
                // 本番は所有者変更禁止トリガーでも保護する。検証中の同一Txだけで外し、サービスの再照合も確認する。
                mutate.CommandText = $"DROP TRIGGER trg_ledger_payment_methods_owner_immutable; UPDATE ledger_payment_methods SET owner_person_id='{owner}',name='所有者変更検証用' WHERE payment_method_id='{payment}'";
                await mutate.ExecuteNonQueryAsync(token);
            }
            return result;
        }
    }

    internal static string[] SeedList(RecordApiFixture f, int count, string? payment = null, string at = "2026-11-10T12:34:56", string shop = "店舗", int start = 0)
    {
        if (count == 0) return [];
        using var connection = f.App.Database.OpenForeignKeyConnection(); using var command = connection.CreateCommand();
        command.CommandText = """
            WITH RECURSIVE numbers(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM numbers WHERE n < @count)
            INSERT INTO ledger_receipts(ledger_id,receipt_id,transaction_at,shop_name,shop_location,payment_method_id,tax_inclusion_mode,tax_rounding_mode,
              recurrence_interval,recurrence_unit,total_amount,memo,revision,created_by_auth_user_id,created_at_utc,updated_by_auth_user_id,updated_at_utc)
            SELECT @ledger,printf('%032x',n+@start),@at,@shop,NULL,@payment,0,'floor',NULL,NULL,n-101,NULL,@revision,@user,'2026-11-10 00:00:00',@user,'2026-11-10 00:00:00'
            FROM numbers
            """;
        foreach (var (key, value) in new (string, object)[] { ("@count", count), ("@start", start), ("@ledger", f.Ledger), ("@at", at),
            ("@shop", shop), ("@payment", payment ?? f.Payment), ("@revision", Guid.NewGuid().ToString("N")), ("@user", f.App.UserId) }) command.Parameters.AddWithValue(key, value);
        command.ExecuteNonQuery(); return Enumerable.Range(start + 1, count).Select(n => n.ToString("x32", CultureInfo.InvariantCulture)).ToArray();
    }

    internal static async Task<JsonObject> ListPage(RecordApiFixture f, string query, int status = 200, HttpClient? client = null)
    {
        using var response = await (client ?? f.Client).GetAsync(f.Root + "?" + query);
        var text = await response.Content.ReadAsStringAsync(); Assert.True((int)response.StatusCode == status, $"Expected {status}, got {(int)response.StatusCode}: {text}");
        return JsonNode.Parse(text)!.AsObject();
    }

    internal sealed class TransferListCommandCapture : DbCommandInterceptor
    {
        public string? Sql { get; private set; }
        public (string Name, object Value)[] Parameters { get; private set; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM ledger_transfers t", StringComparison.Ordinal))
            {
                Sql = command.CommandText; Parameters = command.Parameters.Cast<DbParameter>().Select(p => (p.ParameterName, p.Value!)).ToArray();
            }
            return ValueTask.FromResult(result);
        }
    }
}
