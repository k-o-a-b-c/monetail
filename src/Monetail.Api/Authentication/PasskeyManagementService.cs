using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Monetail.Api.Authentication;

/// <summary>本人へ公開するパスキーの識別子・表示名・登録日時だけを保持する。</summary>
public sealed record PasskeySummary(string Id, string Name, DateTimeOffset CreatedAtUtc);

/// <summary>本人によるパスキー削除の結果をHTTP受付へ渡す。</summary>
public enum PasskeyRemovalResult { Removed, NotFound, LastPasskey, AuthenticationChanged }

/// <summary>現在の本人のパスキーを一覧・追加・削除し、削除時は全セッションを失効させる。</summary>
public sealed class PasskeyManagementService(AuthDbContext context, UserManager<AuthUser> users,
    IPasskeyHandler<AuthUser> passkeys, PasskeyAdditionChallenges challenges, TimeProvider time,
    ILogger<PasskeyManagementService> logger)
{
    /// <summary>現在の本人のパスキーだけを、公開鍵や検証用データを含めず返す。</summary>
    public async Task<PasskeySummary[]> ListAsync(HttpContext http, CancellationToken token)
    {
        var current = (CurrentSession)http.Items[typeof(CurrentSession)]!;
        var user = await context.Users.AsNoTracking().SingleAsync(row => row.Id == current.Session.AuthUserId, token);
        return (await users.GetPasskeysAsync(user)).OrderBy(key => key.CreatedAt)
            .Select(key => new PasskeySummary(WebEncoders.Base64UrlEncode(key.CredentialId), key.Name ?? "パスキー", key.CreatedAt.ToUniversalTime()))
            .ToArray();
    }

    /// <summary>直近に確認した本人とセッションへ、新しいパスキーの作成を結び付ける。</summary>
    public async Task<string?> BeginAsync(HttpContext http, CancellationToken token)
    {
        var current = (CurrentSession)http.Items[typeof(CurrentSession)]!;
        var user = await FindCurrentUserAsync(current, token);
        if (user is null) return null;
        var name = await context.Database.SqlQuery<string>($"SELECT name AS Value FROM m_user WHERE code = {current.UserCode}").SingleAsync(token);
        var options = await passkeys.MakeCreationOptionsAsync(new PasskeyUserEntity
        {
            Id = user.Id, Name = name, DisplayName = name
        }, http);
        if (options.AttestationState is null) return null;
        var expires = time.GetUtcNow().AddMinutes(5);
        var id = challenges.Add(new PasskeyAdditionChallenge(user.Id, current.UserCode, current.Session.IdHash,
            options.AttestationState, expires), http.Request.Cookies[PasskeyAdditionChallenges.CookieName]);
        if (id is null) return null;
        http.Response.Cookies.Append(PasskeyAdditionChallenges.CookieName, id, Cookie(expires));
        return options.CreationOptionsJson;
    }

    /// <summary>標準の登録検証後も本人・期限を確認し、同じアカウントへ鍵だけを追加する。</summary>
    public async Task<bool> CompleteAsync(string credentialJson, HttpContext http, CancellationToken token)
    {
        var current = (CurrentSession)http.Items[typeof(CurrentSession)]!;
        var challenge = challenges.Take(http.Request.Cookies[PasskeyAdditionChallenges.CookieName]);
        http.Response.Cookies.Delete(PasskeyAdditionChallenges.CookieName, Cookie(null));
        if (challenge is null || challenge.AuthUserId != current.Session.AuthUserId || challenge.UserCode != current.UserCode ||
            !CryptographicOperations.FixedTimeEquals(challenge.SessionHash, current.Session.IdHash)) return false;
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        var user = await FindCurrentUserAsync(current, token);
        if (user is null) return false;
        var result = await passkeys.PerformAttestationAsync(new PasskeyAttestationContext
        {
            HttpContext = http, CredentialJson = credentialJson, AttestationState = challenge.AttestationState
        });
        if (!result.Succeeded || result.UserEntity.Id != user.Id || challenge.ExpiresAtUtc <= time.GetUtcNow()) return false;
        result.Passkey.Name = "パスキー";
        if (!(await users.AddOrUpdatePasskeyAsync(user, result.Passkey)).Succeeded)
            throw new InvalidOperationException("パスキーを保存できません。");
        if (challenge.ExpiresAtUtc <= time.GetUtcNow() || await FindCurrentUserAsync(current, token) is null) return false;
        await transaction.CommitAsync(token);
        logger.LogInformation("パスキーを追加しました。認証ID={AuthUserId} 日時={OccurredAtUtc} 結果=成功", user.Id, time.GetUtcNow());
        return true;
    }

    /// <summary>最後の鍵を残して本人の鍵を削除し、全セッションを同時に失効させる。</summary>
    public async Task<PasskeyRemovalResult> RemoveAsync(string id, HttpContext http, CancellationToken token)
    {
        var credentialId = DecodeId(id);
        if (credentialId is null) return PasskeyRemovalResult.NotFound;
        var current = (CurrentSession)http.Items[typeof(CurrentSession)]!;
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        var user = await FindCurrentUserAsync(current, token);
        if (user is null) return PasskeyRemovalResult.AuthenticationChanged;
        var keys = await users.GetPasskeysAsync(user);
        if (!keys.Any(key => key.CredentialId.AsSpan().SequenceEqual(credentialId))) return PasskeyRemovalResult.NotFound;
        if (keys.Count <= 1) return PasskeyRemovalResult.LastPasskey;
        if (!(await users.RemovePasskeyAsync(user, credentialId)).Succeeded)
            throw new InvalidOperationException("パスキーを削除できません。");
        if (await FindCurrentUserAsync(current, token) is null) return PasskeyRemovalResult.AuthenticationChanged;
        var now = time.GetUtcNow().UtcDateTime;
        await context.Sessions.Where(session => session.AuthUserId == user.Id && session.RevokedAtUtc == null)
            .ExecuteUpdateAsync(update => update.SetProperty(session => session.RevokedAtUtc, now), token);
        await transaction.CommitAsync(token);
        await http.SignOutAsync(SessionCookieEvents.Scheme);
        logger.LogInformation("パスキーを削除し全セッションを失効しました。認証ID={AuthUserId} 日時={OccurredAtUtc} 結果=成功", user.Id, time.GetUtcNow());
        return PasskeyRemovalResult.Removed;
    }

    /// <summary>DBで本人・セッション・直近5分を再照合し、Identityの保存と同じアカウントを追跡する。</summary>
    private async Task<AuthUser?> FindCurrentUserAsync(CurrentSession current, CancellationToken token)
    {
        var now = time.GetUtcNow().UtcDateTime;
        return await (from session in context.Sessions
                      join user in context.Users on session.AuthUserId equals user.Id
                      join link in context.UserLinks on user.Id equals link.AuthUserId
                      join person in context.Set<LegacyPerson>() on link.UserCode equals person.Code
                      where session.IdHash == current.Session.IdHash && user.Id == current.Session.AuthUserId &&
                          link.UserCode == current.UserCode && user.IsEnabled && session.RevokedAtUtc == null &&
                          session.ExpiresAtUtc > now && session.VerifiedAtUtc <= now && session.VerifiedAtUtc > now.AddMinutes(-5)
                      select user).SingleOrDefaultAsync(token);
    }

    /// <summary>パスキー識別子を長さ制限付きで検証し、標準のbase64urlから復元する。</summary>
    private static byte[]? DecodeId(string id)
    {
        if (id.Length is 0 or > 1366 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) return null;
        try
        {
            var bytes = WebEncoders.Base64UrlDecode(id);
            return bytes.Length is > 0 and <= 1024 && WebEncoders.Base64UrlEncode(bytes) == id ? bytes : null;
        }
        catch (FormatException) { return null; }
    }

    /// <summary>パスキー追加用の一時Cookieを同じHTTPSホストに限定する。</summary>
    private static CookieOptions Cookie(DateTimeOffset? expires) => new()
    {
        Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax, Path = "/", Expires = expires
    };
}
