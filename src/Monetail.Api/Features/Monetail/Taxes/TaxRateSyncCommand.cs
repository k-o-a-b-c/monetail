using Microsoft.Data.Sqlite;
using Monetail.Api.Authentication;
using Monetail.Api.Persistence;

namespace Monetail.Api.Features.Monetail.Taxes;

/// <summary>Webサーバーを起動せず、既存のDB構成で管理操作を完結する。</summary>
internal static class TaxRateSyncCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            string? file = null; var apply = false; var dryRun = false; var allowDelete = false;
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--file" when file is null && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal):
                        file = args[++i]; break;
                    case "--apply" when !apply: apply = true; break;
                    case "--dry-run" when !dryRun: dryRun = true; break;
                    case "--allow-delete" when !allowDelete: allowDelete = true; break;
                    default: throw Usage();
                }
            }
            if (string.IsNullOrWhiteSpace(file) || apply == dryRun || allowDelete && !apply) throw Usage();
            // 他の管理CLIと同じ30秒の処理期限。秘密やDB接続文字列をエラーに含めない。
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var desired = TaxRateSyncRow.Read(await File.ReadAllTextAsync(file, timeout.Token));
            var builder = WebApplication.CreateBuilder(Array.Empty<string>());
            builder.Logging.ClearProviders();
            var connection = builder.Configuration.GetConnectionString("Monetail");
            if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("ConnectionStrings:Monetailを設定してください。");
            builder.Services.AddAuthDatabase(connection);
            await using var app = builder.Build();
            await using var scope = app.Services.CreateAsyncScope();
            await TaxRateSync.RunAsync(scope.ServiceProvider.GetRequiredService<ApiDbContext>(), desired, apply, allowDelete, Console.Out, timeout.Token);
            return 0;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or FormatException or InvalidCastException or IOException or
            UnauthorizedAccessException or System.Text.Json.JsonException or SqliteException or OperationCanceledException)
        {
            Console.Error.WriteLine(exception is InvalidOperationException ? exception.Message :
                "税率履歴を同期できません。入力ファイル・DB設定・DBの構造・ロック状態を確認してください。適用結果を確認してください。");
            return 1;
        }
    }
    private static InvalidOperationException Usage() => new("使用方法: sync-tax-rates --file PATH (--dry-run | --apply [--allow-delete])");
}
