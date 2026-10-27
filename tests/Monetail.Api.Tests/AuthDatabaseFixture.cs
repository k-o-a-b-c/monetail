using System.Text.Json;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Authentication;
using Monetail.Api.Persistence;

namespace Monetail.Api.Tests;

/// <summary>隔離したAPI用DBを準備し、拒否対象のWindows版DBと保存値を比較する。</summary>
internal sealed class AuthDatabaseFixture : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "monetail-auth-tests-" + Guid.NewGuid().ToString("N"));
    private readonly SqliteConnection? connection;
    public string DatabasePath => Path.Combine(directory, "api.db");
    public string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = DatabasePath, Mode = SqliteOpenMode.ReadWriteCreate,
        ForeignKeys = false, Pooling = false
    }.ToString();

    public AuthDatabaseFixture(bool initializeWindowsDatabase = false, bool createFile = true)
    {
        Directory.CreateDirectory(directory);
        if (!createFile) return;
        connection = new SqliteConnection(ConnectionString);
        connection.Open();
        Execute("PRAGMA user_version=47; PRAGMA journal_mode=DELETE");
        if (!initializeWindowsDatabase) return;
        foreach (var file in new[] { "monetail_schema.sql", "tax_rounding.sql", "revision_tokens.sql" })
            Execute(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, file)));
        Execute(SeedSql);
        foreach (var table in Tables().Where(name => name != "legacy_write_audit"))
        {
            foreach (var operation in new[] { "UPDATE", "DELETE" })
                Execute($"CREATE TRIGGER {QuoteIdentifier("audit_" + operation + "_" + table)} AFTER {operation} ON {QuoteIdentifier(table)} " +
                        $"BEGIN INSERT INTO legacy_write_audit VALUES('{table}','{operation}'); END");
        }
    }

    /// <summary>本番と同じ登録処理から認証ストアを構築する。</summary>
    public ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddAuthDatabase(ConnectionString);
        return services.BuildServiceProvider();
    }

    /// <summary>明示したAPI用DB初期化を新しいDIスコープで実行する。</summary>
    public async Task MigrateAsync()
    {
        await using var services = CreateServices();
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(CancellationToken.None);
    }

    /// <summary>認証接続とは独立した旧接続でSQLを実行する。</summary>
    public void Execute(string sql) => Execute(connection!, sql);

    public static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public object? Scalar(string sql)
    {
        using var command = connection!.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    /// <summary>プロバイダーのFKを有効にした独立接続を開く。</summary>
    public SqliteConnection OpenForeignKeyConnection()
    {
        var options = new SqliteConnectionStringBuilder(ConnectionString) { ForeignKeys = true };
        var result = new SqliteConnection(options.ToString());
        result.Open();
        return result;
    }

    public string[] Tables() => Rows("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name")
        .Select(row => row[0]!).ToArray();

    /// <summary>指定した全表の定義・索引・トリガー・保存型・値と接続設定を記録する。</summary>
    public string Snapshot(IEnumerable<string> tables)
    {
        var result = new StringBuilder();
        foreach (var pragma in new[] { "user_version", "journal_mode", "foreign_keys" })
            result.Append(pragma).Append('=').Append(Scalar("PRAGMA " + pragma)).AppendLine();
        result.AppendLine(JsonSerializer.Serialize(Rows("PRAGMA foreign_key_check")));
        foreach (var table in tables.Order(StringComparer.Ordinal))
        {
            var literal = "'" + table.Replace("'", "''") + "'";
            result.AppendLine(table).AppendLine(JsonSerializer.Serialize(Rows(
                $"SELECT type,name,sql FROM sqlite_master WHERE tbl_name={literal} ORDER BY type,name")));
            var columns = Rows($"PRAGMA table_info({QuoteIdentifier(table)})").Select(row => row[1]!).ToArray();
            var values = string.Join(",", columns.SelectMany(column => new[]
            {
                $"typeof({QuoteIdentifier(column)})", $"quote({QuoteIdentifier(column)})"
            }));
            result.AppendLine(JsonSerializer.Serialize(Rows($"SELECT {values} FROM {QuoteIdentifier(table)} ORDER BY rowid")));
        }
        return result.ToString();
    }

    private List<string?[]> Rows(string sql)
    {
        using var command = connection!.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<string?[]>();
        while (reader.Read())
        {
            var row = new string?[reader.FieldCount];
            for (var index = 0; index < row.Length; index++)
                row[index] = reader.IsDBNull(index) ? null : reader.GetValue(index).ToString();
            rows.Add(row);
        }
        return rows;
    }

    private static string QuoteIdentifier(string name) => '"' + name.Replace("\"", "\"\"") + '"';

    public void Dispose()
    {
        connection?.Dispose();
        Directory.Delete(directory, recursive: true);
    }

    private const string SeedSql = """
        INSERT INTO m_user VALUES(1,'同じ名前','same-account',1),(2,'同じ名前','same-account',2),
          (3,'名前だけ','child-name-only',3),(4,'別の人','person-four',4);
        INSERT INTO m_point_classification VALUES(1,'ポイント',1,0.015);
        INSERT INTO m_payment_classification VALUES(1,'現金',1,1,1,NULL,NULL),(2,'カード',2,NULL,2,'2020-01-01','2030-12-31');
        INSERT INTO m_group_classification VALUES(1,'食費',1,1,1),(2,'交際費',2,1,0);
        INSERT INTO m_shop(code,name,seq,default_point_code,default_group_classification_code,user_code,tax_rounding_mode)
          VALUES(1,'店',1,1,1,1,'half_up');
        INSERT INTO m_tax_rate VALUES(1,0,NULL),(4,8,'2014-04-01'),(5,10,'2019-10-01');
        INSERT INTO t_receipts(seq,date,shop_name,shop_location,tax_type,payment_classification_code,tag,update_user,update_datetime,total,tax_rounding_mode,revision_token)
          VALUES(1,'2026-10-01 12:34:56.789','店',NULL,1,1,NULL,'old-windows-user','2020-01-01',NULL,'floor','legacy-one'),
          (2,'2026-10-02','店','場所',0,2,'引用''符','old-windows-user','2020-01-02',777,'half_up','legacy-two'),
          (3,'2026-10-03','未登録の店','',1,999,'','another-old-user','2020-01-03',0,'floor','legacy-three');
        INSERT INTO t_receipt_groups VALUES(1,1,1,NULL,4),(2,1,2,'混在',5),(3,1,1,'',1);
        INSERT INTO t_receipt_details VALUES(1,1,1,'商品',123,3,7,1.25,1,NULL),
          (2,1,1,'別の商品',-456,1,0,0,1,'原値を維持'),(3,1,1,'ポイント支払い',0,1,30,0,0,'');
        INSERT INTO t_receipt_detail_users VALUES(1,1,1,1),(1,1,1,2),(2,1,1,2);
        INSERT INTO t_receipt_points VALUES(1,1,1,0.015,2,1.5);
        INSERT INTO t_transfers(seq,date,name,fee,from_payment_classification_code,to_payment_classification_code,tag,update_user,update_datetime,revision_token)
          VALUES(1,'2026-10-01','振替',999,1,2,NULL,'old-transfer-user','2020-02-01','old-transfer');
        INSERT INTO t_balances(seq,date,tag,update_user,update_datetime,revision_token)
          VALUES(1,'2026-10-01',NULL,'old-balance-user','2020-03-01','old-balance');
        INSERT INTO t_balance_groups VALUES(1,1,12345,NULL,NULL),(1,2,-123,'評価',151.25);
        INSERT INTO t_payment_memo VALUES(1,NULL,'メモ''引用'),(2,'2026-10-04',NULL);
        INSERT INTO t_exchange_rates VALUES('2026-10-01','USD','JPY',151.345);
        CREATE TABLE legacy_extension(id INTEGER PRIMARY KEY,payload BLOB,note TEXT);
        INSERT INTO legacy_extension VALUES(1,X'00FF80',NULL);
        CREATE TABLE legacy_write_audit(table_name TEXT,operation TEXT);
        """;
}
