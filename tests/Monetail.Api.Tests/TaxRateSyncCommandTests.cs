using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>実CLIプロセス・隔離SQLiteで全履歴同期と非変更・原子性を検証する。</summary>
public partial class AuthenticationCommandTests
{
    private static object SyncRow(string code = "Standard", string from = "2000-01-01", string? to = null, string? rate = "10") =>
        new { taxCategoryCode = code, validFrom = from, validTo = to, rate };
    private static string SyncFile(AuthDatabaseFixture database, params object[] rows)
    {
        var path = database.DatabasePath + ".json";
        File.WriteAllText(path, JsonSerializer.Serialize(new { rates = rows })); return path;
    }
    private static Task<(int ExitCode, string Output, string StandardOutput, string StandardError)> Sync(AuthDatabaseFixture db, string file, params string[] options) =>
        RunAsync(db.ConnectionString, ["sync-tax-rates", "--file", file, .. options], origin: null);

    [Fact]
    public async Task TaxSyncCreatesHistoryWithoutWebSettingsAndReapplyPreservesExactStorage()
    {
        using var db = new AuthDatabaseFixture(); await db.InitializeAsync();
        Assert.Equal(0L, db.Scalar("SELECT count(*) FROM tax_rate_history"));
        var file = SyncFile(db, SyncRow(rate: null), SyncRow("ReducedFood", rate: "0"),
            SyncRow("ReducedNewspaper", rate: "0"), SyncRow("Exempt", rate: null));
        var before = db.Snapshot(db.Tables());
        var dry = await Sync(db, file, "--dry-run");
        Assert.Equal(0, dry.ExitCode); Assert.Empty(dry.StandardError); Assert.Contains("追加: 4, 変更: 0, 削除: 0", dry.StandardOutput);
        Assert.Equal(before, db.Snapshot(db.Tables()));
        var applied = await Sync(db, file, "--apply"); Assert.Equal(0, applied.ExitCode); Assert.Empty(applied.StandardError);
        Assert.DoesNotContain("Now listening", applied.Output); Assert.Contains("同期しました", applied.StandardOutput);
        Assert.Equal(4L, db.Scalar("SELECT count(*) FROM tax_rate_history"));
        Assert.Equal(2L, db.Scalar("SELECT count(*) FROM tax_rate_history WHERE rate IS NULL"));
        Assert.Equal(2L, db.Scalar("SELECT count(*) FROM tax_rate_history WHERE rate='0'"));
        db.Execute("UPDATE tax_rate_history SET rate='0.00' WHERE tax_category_code='ReducedFood'");
        before = db.Snapshot(db.Tables());
        var repeated = await Sync(db, file, "--apply");
        Assert.Equal(0, repeated.ExitCode); Assert.Contains("追加: 0, 変更: 0, 削除: 0", repeated.StandardOutput);
        Assert.Equal(before, db.Snapshot(db.Tables()));
    }

    [Fact]
    public async Task TaxSyncShortensThenAddsAndRateChangeKeepsIdWithNewRevision()
    {
        using var db = new AuthDatabaseFixture(); await db.InitializeAsync();
        var file = SyncFile(db, SyncRow()); Assert.Equal(0, (await Sync(db, file, "--apply")).ExitCode);
        var id = db.Scalar("SELECT id FROM tax_rate_history"); var revision = db.Scalar("SELECT revision FROM tax_rate_history");
        file = SyncFile(db, SyncRow(to: "2029-12-31"), SyncRow(from: "2030-01-01", rate: "1"));
        Assert.Equal(0, (await Sync(db, file, "--apply")).ExitCode);
        Assert.Equal(id, db.Scalar("SELECT id FROM tax_rate_history WHERE valid_from='2000-01-01'"));
        Assert.NotEqual(revision, db.Scalar("SELECT revision FROM tax_rate_history WHERE valid_from='2000-01-01'"));
        var nextId = db.Scalar("SELECT id FROM tax_rate_history WHERE valid_from='2030-01-01'");
        revision = db.Scalar("SELECT revision FROM tax_rate_history WHERE valid_from='2030-01-01'");
        file = SyncFile(db, SyncRow(to: "2029-12-31"), SyncRow(from: "2030-01-01", rate: "8.125000000000000000000000001"));
        Assert.Equal(0, (await Sync(db, file, "--apply")).ExitCode);
        Assert.Equal(nextId, db.Scalar("SELECT id FROM tax_rate_history WHERE valid_from='2030-01-01'"));
        Assert.NotEqual(revision, db.Scalar("SELECT revision FROM tax_rate_history WHERE valid_from='2030-01-01'"));
        Assert.Equal("8.125000000000000000000000001", db.Scalar("SELECT rate FROM tax_rate_history WHERE valid_from='2030-01-01'"));
        Assert.True(Guid.TryParseExact((string)nextId!, "N", out _));
    }

    [Fact]
    public async Task TaxSyncDeletionGuardPreventsEveryChangeAndEmptyArrayRequiresPermission()
    {
        using var db = new AuthDatabaseFixture(); await db.InitializeAsync();
        var file = SyncFile(db, SyncRow(), SyncRow("ReducedFood"));
        Assert.Equal(0, (await Sync(db, file, "--apply")).ExitCode);
        var before = db.Snapshot(db.Tables()); file = SyncFile(db, SyncRow(rate: "1"));
        var dry = await Sync(db, file, "--dry-run"); Assert.Equal(0, dry.ExitCode);
        Assert.Contains("追加: 0, 変更: 1, 削除: 1", dry.StandardOutput);
        Assert.Equal(before, db.Snapshot(db.Tables()));
        var denied = await Sync(db, file, "--apply"); Assert.Equal(1, denied.ExitCode);
        Assert.Contains("--allow-delete", denied.StandardError); Assert.Equal(before, db.Snapshot(db.Tables()));
        Assert.Equal(0, (await Sync(db, file, "--apply", "--allow-delete")).ExitCode);
        Assert.Equal(1L, db.Scalar("SELECT count(*) FROM tax_rate_history")); Assert.Equal("1", db.Scalar("SELECT rate FROM tax_rate_history"));
        file = SyncFile(db); before = db.Snapshot(db.Tables());
        Assert.Equal(0, (await Sync(db, file, "--dry-run")).ExitCode); Assert.Equal(before, db.Snapshot(db.Tables()));
        Assert.Equal(1, (await Sync(db, file, "--apply")).ExitCode); Assert.Equal(before, db.Snapshot(db.Tables()));
        Assert.Equal(0, (await Sync(db, file, "--apply", "--allow-delete")).ExitCode);
        Assert.Equal(0L, db.Scalar("SELECT count(*) FROM tax_rate_history"));
    }

    [Fact]
    public async Task TaxSyncDeterministicDiffAllowsGapsAndExtremeDates()
    {
        using var db = new AuthDatabaseFixture(); await db.InitializeAsync();
        var rows = new[] { SyncRow(from: "9999-12-31", to: "9999-12-31", rate: "0"),
            SyncRow(from: "0001-01-01", to: "0001-01-01", rate: null), SyncRow("ReducedFood", "2024-02-29", "2024-02-29", "8") };
        var file = SyncFile(db, rows); var first = await Sync(db, file, "--dry-run");
        file = SyncFile(db, rows.Reverse().ToArray()); var second = await Sync(db, file, "--dry-run");
        Assert.Equal(0, first.ExitCode); Assert.Equal(0, second.ExitCode); Assert.Equal(first.StandardOutput, second.StandardOutput);
        Assert.Equal(0, (await Sync(db, file, "--apply")).ExitCode);
        // DBの期間重複トリガーは同期後も有効。
        Assert.Throws<SqliteException>(() => db.Execute("INSERT INTO tax_rate_history SELECT 'overlap',tax_category_code,valid_from,valid_to,rate,'revision' FROM tax_rate_history LIMIT 1"));
    }

    public static IEnumerable<object[]> InvalidTaxSyncJson()
    {
        var valid = JsonSerializer.Serialize(SyncRow());
        foreach (var json in new[] { "{}", "[]", "null", "{\"rates\":null}", "{\"rates\":{}}", "{\"rates\":[],\"extra\":1}",
            "{\"rates\":[],\"rates\":[]}", "{\"rates\":[null]}", "{\"rates\":[{}]}", "{", "{\"rates\":[],}" }) yield return [json];
        foreach (var replacement in new[] {
            valid.Replace("Standard", "Unknown"), valid.Replace("2000-01-01", "2000-02-30"),
            valid.Replace("2000-01-01", "0000-01-01"), valid.Replace("2000-01-01", "2000-1-01"),
            valid.Replace("\"validTo\":null", "\"validTo\":\"1999-12-31\""),
            valid.Replace("\"rate\":\"10\"", "\"rate\":10"), valid.Replace("\"rate\":\"10\"", "\"rate\":true"),
            valid.Replace("\"10\"", "\"1e1\""), valid.Replace("\"10\"", "\"1,000\""), valid.Replace("\"10\"", "\" 10\""),
            valid.Replace("\"10\"", "\"79228162514264337593543950336\""),
            valid.Replace("\"10\"", "\"0.12345678901234567890123456789\""),
            valid.Replace("Standard", "Exempt"), valid.Replace("\"rate\":\"10\"", "\"rate\":\"10\",\"extra\":0"),
            valid.Replace("\"rate\":\"10\"", "\"rate\":\"10\",\"rate\":\"10\""), valid.Replace(",\"rate\":\"10\"", "") })
            yield return ["{\"rates\":[" + replacement + "]}"];
        yield return [JsonSerializer.Serialize(new { rates = new[] { SyncRow(), SyncRow() } })];
        yield return [JsonSerializer.Serialize(new { rates = new[] { SyncRow(to: "2020-01-01"), SyncRow(from: "2020-01-01") } })];
        yield return [JsonSerializer.Serialize(new { rates = new[] { SyncRow(), SyncRow(from: "2020-01-01") } })];
    }
    [Theory] [MemberData(nameof(InvalidTaxSyncJson))]
    public async Task TaxSyncRejectsInvalidFileWithoutAnyDatabaseChange(string json)
    {
        using var db = new AuthDatabaseFixture(); await db.InitializeAsync();
        var file = SyncFile(db, SyncRow()); Assert.Equal(0, (await Sync(db, file, "--apply")).ExitCode);
        var before = db.Snapshot(db.Tables()); File.WriteAllText(file, json);
        var result = await Sync(db, file, "--apply", "--allow-delete");
        Assert.Equal(1, result.ExitCode); Assert.NotEmpty(result.StandardError); Assert.Equal(before, db.Snapshot(db.Tables()));
    }

    [Theory]
    [InlineData("update")] [InlineData("insert")]
    public async Task TaxSyncFailureAfterEarlierWritesRollsBackAllTables(string failure)
    {
        using var db = new AuthDatabaseFixture(); await db.InitializeAsync();
        var file = SyncFile(db, SyncRow("Exempt", rate: null), SyncRow("ReducedFood"), SyncRow());
        Assert.Equal(0, (await Sync(db, file, "--apply")).ExitCode);
        db.Execute(failure == "update"
            ? "CREATE TRIGGER fail_sync BEFORE UPDATE ON tax_rate_history WHEN NEW.tax_category_code='Standard' BEGIN SELECT RAISE(ABORT,'injected'); END"
            : "CREATE TRIGGER fail_sync BEFORE INSERT ON tax_rate_history WHEN NEW.valid_from='2030-01-01' BEGIN SELECT RAISE(ABORT,'injected'); END");
        var before = db.Snapshot(db.Tables());
        file = SyncFile(db, SyncRow("ReducedFood", rate: "8"), SyncRow(to: "2029-12-31", rate: "9"), SyncRow(from: "2030-01-01", rate: "1"));
        var result = await Sync(db, file, "--apply", "--allow-delete"); Assert.Equal(1, result.ExitCode);
        Assert.NotEmpty(result.StandardError); Assert.DoesNotContain("同期しました", result.StandardOutput);
        Assert.Equal(before, db.Snapshot(db.Tables()));
    }

    [Theory]
    [InlineData("")] [InlineData("--dry-run")] [InlineData("--file")] [InlineData("--file X")]
    [InlineData("--file X --apply --dry-run")] [InlineData("--file X --dry-run --allow-delete")]
    [InlineData("--file X --allow-delete")] [InlineData("--file X --apply --unknown")]
    [InlineData("--file X --apply --apply")] [InlineData("--file X --file Y --apply")]
    public async Task TaxSyncRejectsInvalidArgumentsWithoutCreatingDatabase(string args)
    {
        using var db = new AuthDatabaseFixture(createFile: false);
        var result = await RunAsync(db.ConnectionString, ["sync-tax-rates", .. args.Split(' ', StringSplitOptions.RemoveEmptyEntries)], origin: null);
        Assert.Equal(1, result.ExitCode); Assert.NotEmpty(result.StandardError); Assert.False(File.Exists(db.DatabasePath));
    }

    [Theory] [InlineData("missing-setting")] [InlineData("missing-db")] [InlineData("empty-db")]
    [InlineData("legacy-db")] [InlineData("missing-file")]
    public async Task TaxSyncRejectsMissingInputOrUninitializedDatabase(string kind)
    {
        using var db = new AuthDatabaseFixture(initializeWindowsDatabase: kind == "legacy-db", createFile: kind is "empty-db" or "legacy-db");
        var file = SyncFile(db, SyncRow()); var existed = File.Exists(db.DatabasePath);
        var bytes = existed ? File.ReadAllBytes(db.DatabasePath) : null;
        if (kind == "missing-file") File.Delete(file);
        var result = await RunAsync(kind == "missing-setting" ? "" : db.ConnectionString,
            ["sync-tax-rates", "--file", file, "--apply"], origin: null);
        Assert.Equal(1, result.ExitCode); Assert.NotEmpty(result.StandardError);
        Assert.Equal(existed, File.Exists(db.DatabasePath)); if (existed) Assert.Equal(bytes, File.ReadAllBytes(db.DatabasePath));
    }

    [Fact]
    public async Task TaxSyncConcurrentIdenticalApplySerializesAndBecomesNoOp()
    {
        using var db = new AuthDatabaseFixture(); await db.InitializeAsync();
        var file = SyncFile(db, SyncRow());
        var results = await Task.WhenAll(Sync(db, file, "--apply"), Sync(db, file, "--apply"));
        Assert.All(results, r => Assert.Equal(0, r.ExitCode));
        Assert.Single(results, r => r.StandardOutput.Contains("追加: 1,"));
        Assert.Single(results, r => r.StandardOutput.Contains("追加: 0, 変更: 0, 削除: 0"));
        Assert.Equal(1L, db.Scalar("SELECT count(*) FROM tax_rate_history"));
    }

    [Fact]
    public async Task TaxSyncWriteLockFailureDoesNotChangeDatabase()
    {
        using var db = new AuthDatabaseFixture(); await db.InitializeAsync(); var file = SyncFile(db, SyncRow());
        var before = db.Snapshot(db.Tables());
        using var connection = db.OpenForeignKeyConnection(); using var transaction = connection.BeginTransaction(deferred: false);
        var options = new SqliteConnectionStringBuilder(db.ConnectionString) { DefaultTimeout = 1 };
        var result = await RunAsync(options.ToString(), ["sync-tax-rates", "--file", file, "--apply"], origin: null);
        Assert.Equal(1, result.ExitCode); Assert.NotEmpty(result.StandardError); transaction.Rollback();
        Assert.Equal(before, db.Snapshot(db.Tables()));
    }
    [Fact]
    public async Task TaxSyncExistingGetReturnsAppliedHistoryAndOtherBusinessDataIsUnchanged()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client(); await app.LoginAsync(client); await LoginTestApp.CsrfAsync(client);
        using var created = await client.PostAsJsonAsync("/ledgers", new { requestId = Guid.NewGuid(), name = "同期試験" });
        Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);
        var ledger = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ledgerId").GetString();
        var otherTables = app.Database.Tables().Except(["tax_rate_history"]).ToArray();
        var before = app.Database.Snapshot(otherTables);
        var file = SyncFile(app.Database, SyncRow(rate: null), SyncRow("ReducedFood", rate: "0"), SyncRow("Exempt", rate: null));
        Assert.Equal(0, (await Sync(app.Database, file, "--apply")).ExitCode);
        Assert.Equal(before, app.Database.Snapshot(otherTables));
        using var response = await client.GetAsync($"/ledgers/{ledger}/tax-rates"); response.EnsureSuccessStatusCode();
        var rows = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rates").EnumerateArray().ToArray();
        Assert.Equal(3, rows.Length);
        Assert.Equal(JsonValueKind.Null, rows.Single(r => r.GetProperty("taxCategoryCode").GetString() == "Standard").GetProperty("rate").ValueKind);
        Assert.Equal("0", rows.Single(r => r.GetProperty("taxCategoryCode").GetString() == "ReducedFood").GetProperty("rate").GetString());
        Assert.All(rows, r => Assert.Equal(4, r.EnumerateObject().Count()));
        // 通常起動・GET・initialize-dbの再実行はいずれも履歴を自動投入/更新しない。
        var snapshot = app.Database.Snapshot(app.Database.Tables());
        Assert.Equal(0, (await RunAsync(app.Database.ConnectionString, ["initialize-db"], origin: null)).ExitCode);
        Assert.Equal(snapshot, app.Database.Snapshot(app.Database.Tables()));
    }

}
