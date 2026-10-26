using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Monetail.Api.Authentication;

/// <summary>名前入力なしのパスキー認証から、固定期限のセッションを作成する。</summary>
public sealed class PasskeyLoginService(AuthDbContext context, UserManager<AuthUser> users,
    IPasskeyHandler<AuthUser> passkeys, IAccountPersonLookup people, LoginChallenges challenges, TimeProvider time,
    ILogger<PasskeyLoginService> logger)
{
    /// <summary>本人を先に指定せず、端末の地域を一時的に結び付けた認証オプションを発行する。</summary>
    public async Task<string?> BeginAsync(string? timeZone, HttpContext http)
    {
        var options = await passkeys.MakeRequestOptionsAsync(null, http);
        if (options.AssertionState is null) return null;
        var expires = time.GetUtcNow().AddMinutes(5);
        var id = challenges.Add(new LoginChallenge(options.AssertionState, SessionLifetime.ResolveTimeZone(timeZone), expires),
            http.Request.Cookies[LoginChallenges.CookieName]);
        if (id is null) return null;
        http.Response.Cookies.Append(LoginChallenges.CookieName, id, Cookie(expires));
        return options.RequestOptionsJson;
    }

    /// <summary>標準の署名検証と鍵状態更新・セッション保存を同じ書込トランザクションで行う。</summary>
    public async Task<bool> CompleteAsync(string credentialJson, HttpContext http, CancellationToken token)
    {
        var challenge = challenges.Take(http.Request.Cookies[LoginChallenges.CookieName]);
        http.Response.Cookies.Delete(LoginChallenges.CookieName, Cookie(null));
        if (challenge is null) return false;
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        var result = await passkeys.PerformAssertionAsync(new PasskeyAssertionContext
        {
            HttpContext = http, CredentialJson = credentialJson, AssertionState = challenge.AssertionState
        });
        if (!result.Succeeded || !result.User.IsEnabled || challenge.ExpiresAtUtc <= time.GetUtcNow()) return false;
        var person = await people.FindAsync(result.User.Id, token);
        if (person is null) return false;
        if (!(await users.AddOrUpdatePasskeyAsync(result.User, result.Passkey)).Succeeded)
            throw new InvalidOperationException("パスキーの認証状態を保存できません。");
        var now = time.GetUtcNow();
        var expires = SessionLifetime.Calculate(now, challenge.Zone);
        var id = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        context.Sessions.Add(new AuthSession
        {
            IdHash = SessionCookieEvents.Hash(id)!, AuthUserId = result.User.Id,
            CreatedAtUtc = now.UtcDateTime, VerifiedAtUtc = now.UtcDateTime, ExpiresAtUtc = expires.UtcDateTime
        });
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        await http.SignInAsync(SessionCookieEvents.Scheme, SessionCookieEvents.Principal(result.User.Id, id),
            new AuthenticationProperties { IsPersistent = true, AllowRefresh = false, IssuedUtc = now, ExpiresUtc = expires });
        logger.LogInformation("ログインしました。認証ID={AuthUserId} 対象人物={PersonCode} 日時={OccurredAtUtc} 結果=成功",
            result.User.Id, person.UserCode, now);
        return true;
    }

    /// <summary>現在のセッションだけをDBで失効させ、Cookieを削除する。</summary>
    public async Task LogoutAsync(HttpContext http, CancellationToken token)
    {
        var hash = SessionCookieEvents.Hash(http.User.FindFirst(SessionCookieEvents.IdClaim)?.Value)!;
        await context.Sessions.Where(row => row.IdHash == hash && row.RevokedAtUtc == null)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.RevokedAtUtc, time.GetUtcNow().UtcDateTime), token);
        await http.SignOutAsync(SessionCookieEvents.Scheme);
        logger.LogInformation("ログアウトしました。認証ID={AuthUserId} 日時={OccurredAtUtc} 結果=成功",
            http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, time.GetUtcNow());
    }

    /// <summary>ログイン用の一時Cookieを同じHTTPSホストに限定する。</summary>
    private static CookieOptions Cookie(DateTimeOffset? expires) => new()
    {
        Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax, Path = "/", Expires = expires
    };
}
