using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Monetail.Api.Persistence;

namespace Monetail.Api.Authentication;

public enum SelfRegistrationStatus { Succeeded, Invalid, Forbidden, RateLimited }
public sealed record SelfRegistrationResult(SelfRegistrationStatus Status, string? OptionsJson = null);

/// <summary>検証済みパスキーと一般アカウント・成功件数を、一つの書込で確定する。</summary>
public sealed class SelfRegistrationService(ApiDbContext context, UserManager<AuthUser> users,
    IPasskeyHandler<AuthUser> passkeys, SelfRegistrationChallenges challenges,
    RegistrationSettings settings, TimeProvider time, ILogger<SelfRegistrationService> logger)
{
    /// <summary>DBへアカウントを作らず、表示名と候補認証IDをチャレンジへ固定する。</summary>
    public async Task<SelfRegistrationResult> BeginAsync(string? displayName, HttpContext http)
    {
        if (settings.Mode != RegistrationMode.Public) return new(SelfRegistrationStatus.Forbidden);
        var name = displayName?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > AuthUser.DisplayNameMaxLength)
            return new(SelfRegistrationStatus.Invalid);
        var id = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var options = await passkeys.MakeCreationOptionsAsync(new PasskeyUserEntity
        {
            Id = id, Name = name, DisplayName = name
        }, http);
        if (options.AttestationState is null) return new(SelfRegistrationStatus.Invalid);
        var expires = time.GetUtcNow().AddMinutes(5);
        var cookie = challenges.Add(new(id, name, options.AttestationState, expires),
            http.Request.Cookies[SelfRegistrationChallenges.CookieName]);
        if (cookie is null) return new(SelfRegistrationStatus.RateLimited);
        http.Response.Cookies.Append(SelfRegistrationChallenges.CookieName, cookie, Cookie(expires));
        return new(SelfRegistrationStatus.Succeeded, options.CreationOptionsJson);
    }

    /// <summary>チャレンジを先に消費し、標準検証後に排他的なSQLite書込で上限と保存を確定する。</summary>
    public async Task<SelfRegistrationResult> CompleteAsync(HttpContext http, CancellationToken token)
    {
        if (settings.Mode != RegistrationMode.Public) return new(SelfRegistrationStatus.Forbidden);
        var challenge = challenges.Take(http.Request.Cookies[SelfRegistrationChallenges.CookieName]);
        http.Response.Cookies.Delete(SelfRegistrationChallenges.CookieName, Cookie(null));
        if (challenge is null) return new(SelfRegistrationStatus.Invalid);
        if (!http.Request.HasJsonContentType()) return new(SelfRegistrationStatus.Invalid);
        // 本文の形式不正でもチャレンジを再利用させない。
        PasskeyEnrollmentEndpoints.AttestationRequest? request;
        try { request = await http.Request.ReadFromJsonAsync<PasskeyEnrollmentEndpoints.AttestationRequest>(token); }
        catch (JsonException) { return new(SelfRegistrationStatus.Invalid); }
        if (request?.Credential.ValueKind != JsonValueKind.Object) return new(SelfRegistrationStatus.Invalid);
        var result = await passkeys.PerformAttestationAsync(new PasskeyAttestationContext
        {
            HttpContext = http, CredentialJson = request.Credential.GetRawText(), AttestationState = challenge.AttestationState
        });
        if (!result.Succeeded || result.UserEntity.Id != challenge.AuthUserId || challenge.ExpiresAtUtc <= time.GetUtcNow())
            return new(SelfRegistrationStatus.Invalid);

        // Microsoft.Data.Sqliteの既定はBEGIN IMMEDIATE。件数の読取前に書込権を確保する。
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        var now = time.GetUtcNow().UtcDateTime;
        if (challenge.ExpiresAtUtc <= time.GetUtcNow()) return new(SelfRegistrationStatus.Invalid);
        var cutoff = now.AddHours(-24);
        if (await context.SelfRegistrationCompletions.CountAsync(row => row.CompletedAtUtc > cutoff, token) >= 100)
            return new(SelfRegistrationStatus.RateLimited);
        // 標準検証後に別要求が同じ鍵を登録した場合も、他アカウントへ付け替えない。
        if (await users.FindByPasskeyIdAsync(result.Passkey.CredentialId) is not null)
            return new(SelfRegistrationStatus.Invalid);
        var user = new AuthUser { Id = challenge.AuthUserId, UserName = challenge.AuthUserId, DisplayName = challenge.DisplayName };
        RequireSuccess(await users.CreateAsync(user));
        result.Passkey.Name = PasskeyNames.ForVerifiedRegistration(result.Passkey);
        RequireSuccess(await users.AddOrUpdatePasskeyAsync(user, result.Passkey));
        user.IsEnabled = true;
        RequireSuccess(await users.UpdateAsync(user));
        var retention = now.AddHours(-48);
        await context.SelfRegistrationCompletions.Where(row => row.CompletedAtUtc < retention).ExecuteDeleteAsync(token);
        context.SelfRegistrationCompletions.Add(new() { CompletedAtUtc = now });
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        logger.LogInformation("匿名自己登録が完了しました。認証ID={AuthUserId} 日時={OccurredAtUtc} 結果=成功", user.Id, now);
        return new(SelfRegistrationStatus.Succeeded);
    }

    private static void RequireSuccess(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException("自己登録を保存できませんでした。");
    }

    private static CookieOptions Cookie(DateTimeOffset? expires) => new()
    {
        HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/", Expires = expires
    };
}
