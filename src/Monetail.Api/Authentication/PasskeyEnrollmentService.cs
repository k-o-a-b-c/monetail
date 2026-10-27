using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Monetail.Api.Authentication;

/// <summary>登録コードの対象へパスキーを登録し、成功時だけ利用可能にする。</summary>
public sealed class PasskeyEnrollmentService(AuthDbContext context, UserManager<AuthUser> users,
    IPasskeyHandler<AuthUser> passkeys, EnrollmentChallenges challenges, TimeProvider time,
    ILogger<PasskeyEnrollmentService> logger)
{
    /// <summary>有効な登録コードの対象に固定した、端末向けの作成オプションを発行する。</summary>
    public async Task<string?> BeginAsync(string? code, string? displayName, HttpContext http, CancellationToken token)
    {
        if (code is null || code.Length != 43 || code.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            return null;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        var stored = await context.EnrollmentCodes.SingleOrDefaultAsync(row => row.CodeHash == hash, token);
        if (!IsUsable(stored)) return null;
        var target = await FindTargetAsync(stored!.AuthUserId, token);
        if (target is null) return null;
        var name = target.DisplayName ?? displayName?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > AuthUser.DisplayNameMaxLength) return null;
        var options = await passkeys.MakeCreationOptionsAsync(new PasskeyUserEntity
        {
            Id = target.Id, Name = name, DisplayName = name
        }, http);
        if (options.AttestationState is null) return null;
        var expires = time.GetUtcNow().AddMinutes(5);
        var id = challenges.Add(new EnrollmentChallenge(target.Id, name,
            hash, options.AttestationState, expires), http.Request.Cookies[EnrollmentChallenges.CookieName]);
        if (id is null) return null;
        http.Response.Cookies.Append(EnrollmentChallenges.CookieName, id, Cookie(expires));
        return options.CreationOptionsJson;
    }

    /// <summary>標準検証後に、表示名・パスキー・利用可否・コード消費を一つのトランザクションで確定する。</summary>
    public async Task<bool> CompleteAsync(string credentialJson, HttpContext http, CancellationToken token)
    {
        var challenge = challenges.Take(http.Request.Cookies[EnrollmentChallenges.CookieName]);
        http.Response.Cookies.Delete(EnrollmentChallenges.CookieName, Cookie(null));
        if (challenge is null) return false;
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        var code = await context.EnrollmentCodes.SingleOrDefaultAsync(row => row.CodeHash == challenge.CodeHash, token);
        if (!IsUsable(code) || code!.AuthUserId != challenge.AuthUserId) return false;
        var target = await FindTargetAsync(challenge.AuthUserId, token);
        if (target is null) return false;
        var result = await passkeys.PerformAttestationAsync(new PasskeyAttestationContext
        {
            HttpContext = http, CredentialJson = credentialJson, AttestationState = challenge.AttestationState
        });
        if (!result.Succeeded || result.UserEntity.Id != challenge.AuthUserId ||
            !IsUsable(code) || challenge.ExpiresAtUtc <= time.GetUtcNow()) return false;
        result.Passkey.Name = "パスキー";
        var user = target;
        if (!(await users.AddOrUpdatePasskeyAsync(user, result.Passkey)).Succeeded)
            throw new InvalidOperationException("パスキーの保存に失敗しました。");
        user.DisplayName ??= challenge.DisplayName;
        user.IsEnabled = true;
        code.ConsumedAtUtc = time.GetUtcNow().UtcDateTime;
        if (!(await users.UpdateAsync(user)).Succeeded)
            throw new InvalidOperationException("アカウントの有効化に失敗しました。");
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        logger.LogInformation("パスキーを登録しました。認証ID={AuthUserId} 日時={OccurredAtUtc} 結果=成功",
            user.Id, time.GetUtcNow());
        return true;
    }

    /// <summary>登録待ちでパスキーのないアカウントを取得する。</summary>
    private async Task<AuthUser?> FindTargetAsync(string id, CancellationToken token)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null || user.IsEnabled || (await users.GetPasskeysAsync(user)).Count > 0) return null;
        return user;
    }

    /// <summary>登録用途・未使用・未失効・期限内のコードだけを受け付ける。</summary>
    private bool IsUsable(EnrollmentCode? code) => code is not null && code.Purpose == "passkey-enrollment" &&
        code.ConsumedAtUtc is null && code.RevokedAtUtc is null && code.ExpiresAtUtc > time.GetUtcNow().UtcDateTime;

    /// <summary>登録チャレンジのCookieをHTTPSの同一ホストだけに制限する。</summary>
    private static CookieOptions Cookie(DateTimeOffset? expires) => new()
    {
        HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/", Expires = expires
    };
}
