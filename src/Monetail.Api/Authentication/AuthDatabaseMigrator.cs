using Microsoft.EntityFrameworkCore;

namespace Monetail.Api.Authentication;

/// <summary>既存DBを確認して認証テーブルだけを追加する。</summary>
public sealed class AuthDatabaseMigrator(AuthDbContext context)
{
    /// <summary>既存ファイルの業務テーブルを確認し、追加移行を適用する。</summary>
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await ValidateLegacyTableAsync("m_user", ["code", "name", "account_name", "seq"], "code", cancellationToken);
            await ValidateLegacyTableAsync("t_receipts", ["seq", "total"], "seq", cancellationToken);
            await context.Database.MigrateAsync(cancellationToken);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    /// <summary>別用途のDBや不完全なスキーマへの誤適用を拒否する。</summary>
    private async Task ValidateLegacyTableAsync(string table, string[] requiredColumns, string key, CancellationToken token)
    {
        using var command = context.Database.GetDbConnection().CreateCommand();
        // テーブル名は上の固定値だけを使用する。
        command.CommandText = $"SELECT name, pk FROM pragma_table_info('{table}') " +
                              $"WHERE EXISTS (SELECT 1 FROM sqlite_master WHERE type='table' AND name='{table}')";
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var primaryKeys = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            columns.Add(reader.GetString(0));
            if (reader.GetInt32(1) != 0) primaryKeys.Add(reader.GetString(0));
        }
        if (!requiredColumns.All(columns.Contains) || primaryKeys.Count != 1 || primaryKeys[0] != key)
            throw new InvalidOperationException($"既存の{table}テーブルを確認できません。業務DBのコピーを指定してください。");
    }
}
