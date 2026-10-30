using System.Threading.RateLimiting;
using Monetail.Api.Authentication;

namespace Monetail.Api.Features.Monetail.Ledgers;

/// <summary>家計簿APIの回数を、検証済みアカウントと用途ごとに制限する。</summary>
public static class LedgerRateLimits
{
    /// <summary>家計簿作成の回数制限を識別する。</summary>
    public const string CreationPolicy = "ledger-creation";
    /// <summary>一覧と個別参照で共有する回数制限を識別する。</summary>
    public const string ReadPolicy = "ledger-read";
    /// <summary>家計簿を開いた記録の更新を識別する。</summary>
    public const string NavigationPolicy = "ledger-navigation";

    /// <summary>作成20回・参照60回の独立した1分枠を登録する。</summary>
    public static void AddLedgerRateLimits(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(CreationPolicy, http => ForAccount(http, 20));
            options.AddPolicy(ReadPolicy, http => ForAccount(http, 60));
            options.AddPolicy(NavigationPolicy, http => ForAccount(http, 60));
        });
    }

    /// <summary>有効なログインのアカウントIDで枠を共有し、未認証要求は認証処理へ渡す。</summary>
    private static RateLimitPartition<string> ForAccount(HttpContext http, int permitLimit) =>
        http.Items.TryGetValue(typeof(CurrentSession), out var value) && value is CurrentSession current
            ? RateLimitPartition.GetFixedWindowLimiter(current.Session.AuthUserId, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            })
            : RateLimitPartition.GetNoLimiter<string>("unauthenticated");
}
