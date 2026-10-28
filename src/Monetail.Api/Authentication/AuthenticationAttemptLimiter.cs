using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Monetail.Api.Authentication;

/// <summary>OAuthの早期拒否と既存認証を、同じIP単位の1分20回の枠で数える。</summary>
public sealed class AuthenticationAttemptLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> limiter = PartitionedRateLimiter.Create<string, string>(
        key => RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
        }));

    /// <summary>認証用ポリシーの要求だけを、標準OAuth処理より先に制限する。</summary>
    public async Task InvokeAsync(HttpContext http, RequestDelegate next)
    {
        if (http.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName == "passkey-authentication")
        {
            using var lease = limiter.AttemptAcquire(http.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            if (!lease.IsAcquired) { http.Response.StatusCode = 429; return; }
        }
        await next(http);
    }

    /// <summary>IPごとの制限状態と内部タイマーを解放する。</summary>
    public void Dispose() => limiter.Dispose();
}
