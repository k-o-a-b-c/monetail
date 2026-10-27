using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Monetail.Api.Authentication;

/// <summary>検証済みセッションとアカウント自身の表示名を、その要求だけに渡す。</summary>
public sealed record CurrentSession(AuthSession Session, string? DisplayName);

/// <summary>Cookieの要求ごとに、セッション・アカウントの利用可否・現在の権限を確認する。</summary>
public sealed class SessionCookieEvents(AuthDbContext context, UserManager<AuthUser> users, TimeProvider time,
    ILogger<SessionCookieEvents> logger) : CookieAuthenticationEvents
{
    public const string Scheme = "Monetail.Session";
    public const string CookieName = "__Host-Monetail.Session";
    public const string IdClaim = "monetail:session";
    public const string FailureKey = "Monetail.AuthFailure";

    /// <summary>暗号保護済みCookieに対応する最新のDB状態を確認し、無効なら拒否する。</summary>
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext cookie)
    {
        try
        {
            var hash = Hash(cookie.Principal?.FindFirstValue(IdClaim));
            var id = cookie.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (hash is null || id is null) { Reject(cookie, 401); return; }
            var token = cookie.HttpContext.RequestAborted;
            var session = await context.Sessions.AsNoTracking().SingleOrDefaultAsync(row => row.IdHash == hash, token);
            if (session is null || session.AuthUserId != id || session.RevokedAtUtc is not null ||
                session.ExpiresAtUtc <= time.GetUtcNow().UtcDateTime)
            { Reject(cookie, 401); return; }
            var user = await context.Users.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token);
            if (user is null || !user.IsEnabled) { Reject(cookie, 401); return; }
            var principal = Principal(id, cookie.Principal!.FindFirstValue(IdClaim)!);
            var identity = (ClaimsIdentity)principal.Identity!;
            if (user.DisplayName is not null) identity.AddClaim(new Claim(ClaimTypes.Name, user.DisplayName));
            foreach (var role in await users.GetRolesAsync(user)) identity.AddClaim(new Claim(ClaimTypes.Role, role));
            cookie.ReplacePrincipal(principal);
            cookie.ShouldRenew = false;
            cookie.HttpContext.Items[typeof(CurrentSession)] = new CurrentSession(session, user.DisplayName);
        }
        catch (Exception exception) when (exception is SqliteException or DbUpdateException or InvalidOperationException)
        {
            logger.LogWarning("セッションを確認できません。エラー種類={ErrorType}", exception.GetType().Name);
            Reject(cookie, 503);
        }
    }

    /// <summary>未認証のAPI要求には、画面転送をせず401を返す。</summary>
    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = 401;
        return Task.CompletedTask;
    }

    /// <summary>権限不足のAPI要求には、画面転送をせず403を返す。</summary>
    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = 403;
        return Task.CompletedTask;
    }

    /// <summary>保護Cookieへ渡す認証IDと、推測できないセッション識別子だけを構成する。</summary>
    public static ClaimsPrincipal Principal(string userId, string sessionId) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, userId), new Claim(IdClaim, sessionId)], Scheme));

    /// <summary>セッション識別子を検証し、DB照合用のハッシュへ変換する。</summary>
    public static byte[]? Hash(string? id) => id is { Length: 43 } &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            ? SHA256.HashData(Encoding.UTF8.GetBytes(id)) : null;

    /// <summary>認証を取り消し、保護APIへ返す拒否理由の状態コードを保持する。</summary>
    private static void Reject(CookieValidatePrincipalContext context, int status)
    {
        context.RejectPrincipal();
        context.HttpContext.Items[FailureKey] = status;
    }
}
