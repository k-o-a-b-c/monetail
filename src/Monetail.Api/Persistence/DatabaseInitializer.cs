using System.Data;
using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Monetail.Api.Persistence;

/// <summary>空の新規API用DBだけを作成し、既存ファイルには検証以外の操作をしない。</summary>
public sealed class DatabaseInitializer(ApiDbContext context)
{
    private static readonly ConcurrentDictionary<IModel, Lazy<Task<Dictionary<string, string>>>> schemas = new();

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
            if ((await ReadSchemaAsync(probe, null, token)).Count != 0)
            {
                await ValidateAsync(context, probe, null, strict: true, token);
                return;
            }
        }
        settings.Mode = SqliteOpenMode.ReadWriteCreate;
        await using (var create = new SqliteConnection(settings.ToString())) await create.OpenAsync(token);
        // 通常の認証/業務接続はReadWriteのまま。ファイル作成はこの明示コマンドだけで行う。
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        var dbTransaction = transaction.GetDbTransaction();
        if ((await ReadSchemaAsync(connection, dbTransaction, token)).Count == 0)
            await DatabaseSchema.CreateAsync(context, token);
        // 他プロセスが検証後にファイルを変えた場合も、構造を修復せず拒否する。
        await ValidateAsync(context, connection, dbTransaction, strict: true, token);
        await transaction.CommitAsync(token);
    }

    /// <summary>認証/管理操作は現在の必須構造を検証するだけで、DBを作成・更新しない。</summary>
    public static async Task EnsureReadyAsync(ApiDbContext database, CancellationToken token = default)
    {
        var connection = (SqliteConnection)database.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened) await connection.OpenAsync(token);
        try { await ValidateAsync(database, connection, database.Database.CurrentTransaction?.GetDbTransaction(), strict: false, token); }
        finally { if (opened) await connection.CloseAsync(); }
    }

    private static async Task ValidateAsync(ApiDbContext database, SqliteConnection connection, DbTransaction? transaction, bool strict, CancellationToken token)
    {
        var actual = await ReadSchemaAsync(connection, transaction, token);
        var model = database.GetService<IDesignTimeModel>().Model;
        var expected = await schemas.GetOrAdd(model, value => new(() => ExpectedSchemaAsync(value))).Value.WaitAsync(token);
        if (expected.Any(row => !actual.TryGetValue(row.Key, out var sql) || sql != row.Value) || strict && actual.Count != expected.Count)
            throw new InvalidOperationException("現在のAPI用DB構造と一致しません。新規DBにinitialize-dbを実行してください。既存DBは変更していません。");
        // 初期マスタの欠落も修復しない。税率履歴や認証・業務データの値は変更しない。
        await using var command = connection.CreateCommand(); command.Transaction = (SqliteTransaction?)transaction;
        command.CommandText = "SELECT (SELECT COUNT(*) FROM tax_categories WHERE code IN ('Standard','ReducedFood','ReducedNewspaper','Exempt')) = 4 AND " +
            "EXISTS(SELECT 1 FROM OpenIddictApplications WHERE Id='monetail-windows' AND ClientId='monetail-windows')";
        if (Convert.ToInt64(await command.ExecuteScalarAsync(token)) != 1)
            throw new InvalidOperationException("API用DBの初期マスタが不足しています。DBは変更していません。");
        if (strict)
        {
            command.CommandText = "PRAGMA foreign_key_check";
            await using var reader = await command.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token))
                throw new InvalidOperationException("API用DBの参照整合性が壊れています。DBは変更していません。");
        }
    }

    private static async Task<Dictionary<string, string>> ExpectedSchemaAsync(IModel model)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(connection).UseModel(model);
        await using var expected = new ApiDbContext(options.Options);
        await using var transaction = await expected.Database.BeginTransactionAsync();
        await DatabaseSchema.CreateAsync(expected, CancellationToken.None);
        return await ReadSchemaAsync(connection, transaction.GetDbTransaction(), CancellationToken.None);
    }

    private static async Task<Dictionary<string, string>> ReadSchemaAsync(SqliteConnection connection, DbTransaction? transaction, CancellationToken token)
    {
        await using var command = connection.CreateCommand(); command.Transaction = (SqliteTransaction?)transaction;
        command.CommandText = "SELECT type,name,sql FROM sqlite_schema WHERE name NOT LIKE 'sqlite_%' AND sql IS NOT NULL ORDER BY type,name";
        await using var reader = await command.ExecuteReaderAsync(token);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(token)) result.Add(reader.GetString(0) + ":" + reader.GetString(1), reader.GetString(2).Trim());
        return result;
    }

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
