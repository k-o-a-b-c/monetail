using Monetail.Api.Authentication;
using Monetail.Api.Features.Monetail.Accounts;

var command = args.FirstOrDefault();
var isAuthCommand = command is "migrate-auth-db" or "initialize-admin" or "issue-enrollment-code" or
    "revoke-account" or "reset-authentication";
var configurationArgs = isAuthCommand ? args[1..] : args;
string? authUserId = null;
// 緊急復旧を明示したときだけ、最後の管理者の通常停止保護を解除する。
var emergencyRecovery = false;
if (command is "issue-enrollment-code" or "revoke-account")
{
    if (args.Length < 3 || args[1] != "--account-id" || string.IsNullOrWhiteSpace(args[2]) ||
        args[2].StartsWith("--", StringComparison.Ordinal))
    {
        Console.Error.WriteLine("アカウントIDを明示してください: issue-enrollment-code / revoke-account --account-id ID");
        Environment.ExitCode = 1;
        return;
    }
    authUserId = args[2];
    configurationArgs = args[3..];
    if (command == "revoke-account" && configurationArgs.FirstOrDefault() == "--emergency-recovery")
    {
        emergencyRecovery = true;
        configurationArgs = configurationArgs[1..];
    }
}
if (command == "reset-authentication")
{
    if (args.Length < 2 || args[1] != "--after-restore")
    {
        Console.Error.WriteLine("復元後の全失効は、サービス停止後にreset-authentication --after-restoreを明示してください。");
        Environment.ExitCode = 1;
        return;
    }
    configurationArgs = args[2..];
}
if (isAuthCommand && configurationArgs.Any(argument =>
    new[] { "--account-id", "--person-code", "--emergency-recovery", "--after-restore" }.Any(option =>
        argument == option || argument.StartsWith(option + "=", StringComparison.Ordinal))))
{
    Console.Error.WriteLine("管理コマンドの操作オプションが重複しているか、指定位置が不正です。");
    Environment.ExitCode = 1;
    return;
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
    builder.Services.AddScoped<AdminAccountService>();
    builder.Services.AddScoped<AdminAccountProvisioningService>();
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
        else if (command is "revoke-account" or "reset-authentication")
        {
            var revocation = scope.ServiceProvider.GetRequiredService<AuthenticationRevocationService>();
            if (command == "revoke-account")
                await revocation.RevokeAccountAsync(authUserId!, emergencyRecovery, timeout.Token);
            else
                await revocation.ResetAfterRestoreAsync(timeout.Token);
            Console.WriteLine("認証情報を全失効しました。登録コードは発行していません。");
        }
        else
        {
            var provisioning = scope.ServiceProvider.GetRequiredService<AdminProvisioningService>();
            var issued = command == "initialize-admin"
                ? await provisioning.InitializeAdminAsync(timeout.Token)
                : await provisioning.IssueEnrollmentCodeAsync(authUserId!, timeout.Token);
            if (issued is null)
                Console.WriteLine("初期管理者は作成済みです。コードが必要ならissue-enrollment-codeを実行してください。");
            else
            {
                Console.WriteLine($"アカウントID: {issued.AuthUserId}");
                Console.WriteLine($"登録コード: {issued.Code}");
                Console.WriteLine($"有効期限（UTC）: {issued.ExpiresAtUtc:O}");
            }
        }
    }
    catch (Exception exception) when (exception is InvalidOperationException or
        Microsoft.Data.Sqlite.SqliteException or Microsoft.EntityFrameworkCore.DbUpdateException or OperationCanceledException)
    {
        app.Logger.LogWarning("管理コマンド失敗。操作={Command} 認証ID={AuthUserId} 日時={OccurredAtUtc} エラー種類={ErrorType}",
            command, authUserId, DateTimeOffset.UtcNow, exception.GetType().Name);
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
    app.MapAdminAccounts();
}
app.MapHealthChecks("/health").AllowAnonymous();
app.Run();

/// <summary>統合テストからAPIの起動処理を参照する。</summary>
public partial class Program;
