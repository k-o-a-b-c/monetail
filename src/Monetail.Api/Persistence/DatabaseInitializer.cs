using System.Collections.Concurrent;
using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Monetail.Api.Persistence;

/// <summary>新規API用DBを検証し、明示的に初期作成・追加移行する。</summary>
public sealed class DatabaseInitializer(ApiDbContext context)
{
    /// <summary>移行段階ごとの正規構造を隔離DBから生成して再利用する。</summary>
    private static readonly ConcurrentDictionary<string, Lazy<Task<Dictionary<string, string>>>> schemas = new();

    /// <summary>同じファイルの初期化を排他化し、検証済みのDBだけを移行する。</summary>
    public async Task InitializeAsync(CancellationToken token = default)
    {
        var settings = new SqliteConnectionStringBuilder(context.Database.GetConnectionString());
        var path = Path.GetFullPath(settings.DataSource);
        await using var operationLock = await AcquireLockAsync(path + ".initialize.lock", token);
        if (File.Exists(path))
        {
            settings.Mode = SqliteOpenMode.ReadOnly;
            await using var probe = new SqliteConnection(settings.ToString());
            await probe.OpenAsync(token);
            await ValidateAsync(context, probe, requireCurrent: false, token);
        }
        settings.Mode = SqliteOpenMode.ReadWriteCreate;
        await using (var create = new SqliteConnection(settings.ToString()))
            await create.OpenAsync(token);
        // 初期作成以外の接続はReadWriteのままとし、通常操作によるファイル作成を防ぐ。
        await context.Database.MigrateAsync(token);
        await context.Database.OpenConnectionAsync(token);
        try { await ValidateAsync(context, (SqliteConnection)context.Database.GetDbConnection(), requireCurrent: true, token); }
        finally { await context.Database.CloseConnectionAsync(); }
    }

    /// <summary>新構成の移行完了と移行ロックを確認し、未準備の認証・管理操作を拒否する。</summary>
    public static async Task EnsureReadyAsync(ApiDbContext database, CancellationToken token = default)
    {
        var connection = (SqliteConnection)database.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened) await connection.OpenAsync(token);
        try { await ValidateAsync(database, connection, requireCurrent: true, token, checkStructure: false); }
        finally { if (opened) await connection.CloseAsync(); }
    }

    /// <summary>既知の履歴の連続性と、その移行段階の表・索引・FK定義を照合する。</summary>
    private static async Task ValidateAsync(ApiDbContext database, SqliteConnection connection,
        bool requireCurrent, CancellationToken token, bool checkStructure = true)
    {
        var actual = await ReadSchemaAsync(connection, token);
        if (actual.ContainsKey("table:__EFMigrationsLock"))
        {
            await using var locked = connection.CreateCommand();
            locked.CommandText = "SELECT COUNT(*) FROM __EFMigrationsLock";
            if (Convert.ToInt64(await locked.ExecuteScalarAsync(token)) != 0)
                throw new InvalidOperationException("移行ロックが残っています。サービス停止・バックアップ後に状態を確認してください。");
        }
        var known = database.Database.GetMigrations().ToArray();
        var applied = new List<string>();
        if (actual.ContainsKey("table:" + ApiDbContext.HistoryTable))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT MigrationId FROM {ApiDbContext.HistoryTable} ORDER BY MigrationId";
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) applied.Add(reader.GetString(0));
        }
        if (applied.Count > known.Length || !applied.SequenceEqual(known.Take(applied.Count)))
            throw new InvalidOperationException("未知または不連続な移行履歴です。DBを変更せず終了します。");
        if (requireCurrent && (known.Length == 0 || applied.Count != known.Length))
            throw new InvalidOperationException("先にinitialize-dbを実行してください。");
        if (!checkStructure) return;
        var stage = applied.LastOrDefault() ?? "0";
        var expected = await schemas.GetOrAdd(stage, id => new(() => ExpectedSchemaAsync(id))).Value;
        // 移行開始前に停止したDBでは、EF管理表だけが一部残る場合も再開対象にする。
        if (actual.Any(row => !expected.TryGetValue(row.Key, out var sql) || sql != row.Value) ||
            (applied.Count > 0 && actual.Count != expected.Count))
            throw new InvalidOperationException("新規API用DBの構造と履歴が一致しません。旧DBの引継ぎは移行SQLで行ってください。");
    }

    /// <summary>対象の移行までをメモリーDBへ適用し、比較する正規の構造を生成する。</summary>
    private static async Task<Dictionary<string, string>> ExpectedSchemaAsync(string stage)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApiDbContext>();
        options.UseSqlite(connection, sqlite => sqlite.MigrationsHistoryTable(ApiDbContext.HistoryTable));
        await using var expected = new ApiDbContext(options.Options);
        await expected.GetService<IMigrator>().MigrateAsync(stage);
        return await ReadSchemaAsync(connection, CancellationToken.None);
    }

    /// <summary>SQLite内部の自動生成オブジェクトを除き、保存されたDDLを読み取る。</summary>
    private static async Task<Dictionary<string, string>> ReadSchemaAsync(SqliteConnection connection, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT type,name,sql FROM sqlite_schema WHERE name NOT LIKE 'sqlite_%' AND sql IS NOT NULL ORDER BY type,name";
        await using var reader = await command.ExecuteReaderAsync(token);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(token))
            result.Add(reader.GetString(0) + ":" + reader.GetString(1), reader.GetString(2).Trim());
        return result;
    }

    /// <summary>初期化の同時実行を待機期限内で直列化し、プロセス終了時にはOSにロックを解放させる。</summary>
    private static async Task<FileStream> AcquireLockAsync(string path, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (File.Exists(path)) { await Task.Delay(100, token); }
        }
    }
}
