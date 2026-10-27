using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Monetail.Api.Persistence;

namespace Monetail.Api.Authentication;

/// <summary>現在の本人をパスキーで再確認し、既存セッションの確認日時だけを更新する。</summary>
public sealed class PasskeyReauthenticationService(ApiDbContext context, UserManager<AuthUser> users,
    IPasskeyHandler<AuthUser> passkeys, ReauthenticationChallenges challenges, TimeProvider time,
    ILogger<PasskeyReauthenticationService> logger)
{
    /// <summary>現在の本人のパスキーだけを使う再確認を、既存セッションに結び付けて開始する。</summary>
    public async Task<string?> BeginAsync(HttpContext http, CancellationToken token)
    {
        var current = (CurrentSession)http.Items[typeof(CurrentSession)]!;
        var user = await context.Users.AsNoTracking().SingleOrDefaultAsync(row => row.Id == current.Session.AuthUserId, token);
        if (user is null || !user.IsEnabled) return null;
        var options = await passkeys.MakeRequestOptionsAsync(user, http);
        if (options.AssertionState is null) return null;
        var expires = time.GetUtcNow().AddMinutes(5);
        var id = challenges.Add(new ReauthenticationChallenge(options.AssertionState, user.Id,
            current.Session.IdHash, expires), http.Request.Cookies[ReauthenticationChallenges.CookieName]);
        if (id is null) return null;
        http.Response.Cookies.Append(ReauthenticationChallenges.CookieName, id, Cookie(expires));
        return options.RequestOptionsJson;
    }

    /// <summary>同じ本人・セッションの署名を検証し、鍵状態と確認日時を同じトランザクションで更新する。</summary>
    public async Task<bool> CompleteAsync(string credentialJson, HttpContext http, CancellationToken token)
    {
        var current = (CurrentSession)http.Items[typeof(CurrentSession)]!;
        var challenge = challenges.Take(http.Request.Cookies[ReauthenticationChallenges.CookieName]);
        http.Response.Cookies.Delete(ReauthenticationChallenges.CookieName, Cookie(null));
        if (challenge is null || challenge.AuthUserId != current.Session.AuthUserId ||
            !CryptographicOperations.FixedTimeEquals(challenge.SessionHash, current.Session.IdHash)) return false;
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        var result = await passkeys.PerformAssertionAsync(new PasskeyAssertionContext
        {
            HttpContext = http, CredentialJson = credentialJson, AssertionState = challenge.AssertionState
        });
        var now = time.GetUtcNow();
        if (!result.Succeeded || result.User.Id != challenge.AuthUserId || !result.User.IsEnabled ||
            challenge.ExpiresAtUtc <= now) return false;
        if (!(await users.AddOrUpdatePasskeyAsync(result.User, result.Passkey)).Succeeded)
            throw new InvalidOperationException("パスキーの認証状態を保存できません。");
        now = time.GetUtcNow();
        if (challenge.ExpiresAtUtc <= now) return false;
        var updated = await context.Sessions.Where(row => row.IdHash == challenge.SessionHash &&
            row.AuthUserId == challenge.AuthUserId && row.RevokedAtUtc == null && row.ExpiresAtUtc > now.UtcDateTime)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.VerifiedAtUtc, now.UtcDateTime), token);
        if (updated != 1) return false;
        await transaction.CommitAsync(token);
        logger.LogInformation("本人を再確認しました。認証ID={AuthUserId} 日時={OccurredAtUtc} 結果=成功",
            challenge.AuthUserId, now);
        return true;
    }

    /// <summary>本人再確認用の一時Cookieを同じHTTPSホストに限定する。</summary>
    private static CookieOptions Cookie(DateTimeOffset? expires) => new()
    {
        Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax, Path = "/", Expires = expires
    };
}
