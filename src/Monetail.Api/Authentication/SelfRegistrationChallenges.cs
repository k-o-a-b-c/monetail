using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace Monetail.Api.Authentication;

/// <summary>未保存の認証ID・表示名と標準WebAuthn検証状態を固定する。</summary>
public sealed record SelfRegistrationChallenge(string AuthUserId, string DisplayName,
    string AttestationState, DateTimeOffset ExpiresAtUtc);

/// <summary>招待登録と分離した、最大128件・5分・単回使用のブラウザーチャレンジ。</summary>
public sealed class SelfRegistrationChallenges(TimeProvider time)
{
    public const string CookieName = "__Host-Monetail.SelfRegistration";
    private readonly object gate = new();
    private readonly Dictionary<string, SelfRegistrationChallenge> entries = new(StringComparer.Ordinal);

    /// <summary>期限切れと同じブラウザーの旧要求を除き、容量内で新しい識別子を発行する。</summary>
    public string? Add(SelfRegistrationChallenge challenge, string? previous)
    {
        lock (gate)
        {
            var now = time.GetUtcNow();
            foreach (var key in entries.Where(pair => pair.Value.ExpiresAtUtc <= now).Select(pair => pair.Key).ToArray())
                entries.Remove(key);
            if (previous is not null) entries.Remove(previous);
            if (entries.Count >= 128) return null;
            var id = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            entries.Add(id, challenge);
            return id;
        }
    }

    /// <summary>検証失敗・同時送信でも再利用できないよう、取出しと削除を一体で行う。</summary>
    public SelfRegistrationChallenge? Take(string? id)
    {
        lock (gate)
            return id is not null && entries.Remove(id, out var entry) && entry.ExpiresAtUtc > time.GetUtcNow()
                ? entry : null;
    }
}
