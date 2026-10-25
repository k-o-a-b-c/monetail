using Monetail.Api.Authentication;
using System.Globalization;

var command = args.FirstOrDefault();
var isAuthCommand = command is "migrate-auth-db" or "initialize-admin" or "issue-enrollment-code";
var configurationArgs = isAuthCommand ? args[1..] : args;
int? personCode = null;
if (command is "initialize-admin" or "issue-enrollment-code")
{
    if (args.Length < 3 || args[1] != "--person-code" ||
        !int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCode))
    {
        Console.Error.WriteLine("人物コードを明示してください: initialize-admin または issue-enrollment-code --person-code 整数");
        Environment.ExitCode = 1;
        return;
    }
    personCode = parsedCode;
    configurationArgs = args[3..];
}
var builder = WebApplication.CreateBuilder(configurationArgs);
builder.Services.AddHealthChecks();
var connectionString = builder.Configuration.GetConnectionString("Monetail");
if (!string.IsNullOrWhiteSpace(connectionString))
    builder.Services.AddAuthDatabase(connectionString);
var passkeySettings = isAuthCommand ? null : PasskeySettings.Read(builder.Configuration);
if (passkeySettings is not null)
{
    if (string.IsNullOrWhiteSpace(connectionString))
        throw new InvalidOperationException("パスキー登録にはConnectionStrings:Monetailを指定してください。");
    builder.Services.AddPasskeyWeb(passkeySettings);
}

var app = builder.Build();
if (isAuthCommand)
{
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        Console.Error.WriteLine("ConnectionStrings:Monetailに既存DBを指定してください。");
        Environment.ExitCode = 1;
        await app.DisposeAsync();
        return;
    }
    try
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var scope = app.Services.CreateAsyncScope();
        if (command == "migrate-auth-db")
        {
            await scope.ServiceProvider.GetRequiredService<AuthDatabaseMigrator>().MigrateAsync(timeout.Token);
            Console.WriteLine("認証DBの追加移行が完了しました。");
        }
        else
        {
            var provisioning = scope.ServiceProvider.GetRequiredService<AdminProvisioningService>();
            var issued = command == "initialize-admin"
                ? await provisioning.InitializeAdminAsync(personCode!.Value, timeout.Token)
                : await provisioning.IssueEnrollmentCodeAsync(personCode!.Value, timeout.Token);
            if (issued is null)
                Console.WriteLine("初期管理者は作成済みです。コードが必要ならissue-enrollment-codeを実行してください。");
            else
            {
                Console.WriteLine($"登録コード: {issued.Code}");
                Console.WriteLine($"有効期限（UTC）: {issued.ExpiresAtUtc:O}");
            }
        }
    }
    catch (Exception exception) when (exception is InvalidOperationException or
        Microsoft.Data.Sqlite.SqliteException or Microsoft.EntityFrameworkCore.DbUpdateException or OperationCanceledException)
    {
        app.Logger.LogWarning("管理コマンド失敗。操作={Command} 対象人物={PersonCode} 日時={OccurredAtUtc} エラー種類={ErrorType}",
            command, personCode, DateTimeOffset.UtcNow, exception.GetType().Name);
        Console.Error.WriteLine(exception is InvalidOperationException
            ? exception.Message : "認証DBの管理処理に失敗しました。接続設定・DBの状態を確認してください。");
        Environment.ExitCode = 1;
    }
    await app.DisposeAsync();
    return;
}
if (passkeySettings is not null)
{
    app.Logger.LogInformation("セッション期限の既定地域={TimeZone}", TimeZoneInfo.Local.Id);
    app.MapPasskeyWeb(passkeySettings);
}
app.MapHealthChecks("/health").AllowAnonymous();
app.Run();

/// <summary>統合テストからAPIの起動処理を参照する。</summary>
public partial class Program;
