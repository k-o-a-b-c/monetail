using Monetail.Api.Authentication;

var migrateAuthDatabase = args.Length > 0 && args[0] == "migrate-auth-db";
var builder = WebApplication.CreateBuilder(migrateAuthDatabase ? args[1..] : args);
builder.Services.AddHealthChecks();
var connectionString = builder.Configuration.GetConnectionString("Monetail");
if (!string.IsNullOrWhiteSpace(connectionString))
    builder.Services.AddAuthDatabase(connectionString);

var app = builder.Build();
if (migrateAuthDatabase)
{
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        Console.Error.WriteLine("ConnectionStrings:Monetailに既存DBを指定してください。");
        Environment.ExitCode = 1;
        return;
    }
    try
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AuthDatabaseMigrator>().MigrateAsync(timeout.Token);
        Console.WriteLine("認証DBの追加移行が完了しました。");
    }
    catch (Exception exception) when (exception is InvalidOperationException or
        Microsoft.Data.Sqlite.SqliteException or OperationCanceledException)
    {
        Console.Error.WriteLine("認証DBの追加移行に失敗しました。既存DB・接続設定・移行ロックを確認してください。");
        Environment.ExitCode = 1;
    }
    await app.DisposeAsync();
    return;
}
app.MapHealthChecks("/health");
app.Run();

/// <summary>統合テストからAPIの起動処理を参照する。</summary>
public partial class Program;
