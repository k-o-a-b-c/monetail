using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Monetail.Api.Persistence;

namespace Monetail.Api.Authentication;

/// <summary>CookieとAPIトークンに対応するセッション・アカウント・役割を最新DBで検証する。</summary>
public sealed class SessionValidator(ApiDbContext context, TimeProvider time)
{
    /// <summary>安定した認証IDとセッション識別子から、現在も有効な本人情報を取得する。</summary>
    public async Task<CurrentSession?> FindAsync(string? userId, string? sessionId, CancellationToken token)
    {
        var hash = SessionCookieEvents.Hash(sessionId);
        if (hash is null || userId is null) return null;
        var session = await context.Sessions.AsNoTracking().SingleOrDefaultAsync(row => row.IdHash == hash, token);
        if (session is null || session.AuthUserId != userId || session.RevokedAtUtc is not null ||
            session.ExpiresAtUtc <= time.GetUtcNow().UtcDateTime) return null;
        var user = await context.Users.AsNoTracking().SingleOrDefaultAsync(row => row.Id == userId, token);
        return user is { IsEnabled: true } ? new CurrentSession(session, user.DisplayName) : null;
    }

    /// <summary>検証済みアカウントの表示名と現在の全体ロールをプリンシパルへ反映する。</summary>
    public async Task<ClaimsPrincipal> PrincipalAsync(CurrentSession current, string sessionId, string scheme, CancellationToken token = default)
    {
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, current.Session.AuthUserId),
            new Claim(SessionCookieEvents.IdClaim, sessionId)], scheme);
        if (current.DisplayName is not null) identity.AddClaim(new Claim(ClaimTypes.Name, current.DisplayName));
        var roles = await (from member in context.UserRoles.AsNoTracking()
                           join role in context.Roles.AsNoTracking() on member.RoleId equals role.Id
                           where member.UserId == current.Session.AuthUserId
                           select role.Name).ToListAsync(token);
        foreach (var role in roles)
            if (role is not null) identity.AddClaim(new Claim(ClaimTypes.Role, role));
        return new ClaimsPrincipal(identity);
    }
}
