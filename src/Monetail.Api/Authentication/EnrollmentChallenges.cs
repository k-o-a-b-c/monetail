using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace Monetail.Api.Authentication;

/// <summary>登録対象とIdentityの検証状態を、5分だけブラウザーに結び付ける。</summary>
public sealed record EnrollmentChallenge(string AuthUserId, string DisplayName, byte[] CodeHash,
    string AttestationState, DateTimeOffset ExpiresAtUtc);

/// <summary>再起動で破棄する登録チャレンジを、件数制限付きで原子的に管理する。</summary>
public sealed class EnrollmentChallenges(TimeProvider time)
{
    public const string CookieName = "__Host-Monetail.Enrollment";
    private readonly object gate = new();
    private readonly Dictionary<string, EnrollmentChallenge> entries = new(StringComparer.Ordinal);

    /// <summary>古いチャレンジを置き換え、推測できないブラウザー識別子を返す。</summary>
    public string? Add(EnrollmentChallenge challenge, string? previous)
    {
        lock (gate)
        {
            foreach (var key in entries.Where(pair => pair.Value.ExpiresAtUtc <= time.GetUtcNow())
                .Select(pair => pair.Key).ToArray()) entries.Remove(key);
            if (previous is not null) entries.Remove(previous);
            if (entries.Count >= 1024) return null;
            var id = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            entries.Add(id, challenge);
            return id;
        }
    }

    /// <summary>同じチャレンジを一度だけ取り出し、失敗・同時要求からの再利用も拒否する。</summary>
    public EnrollmentChallenge? Take(string? id)
    {
        lock (gate)
            return id is not null && entries.Remove(id, out var entry) && entry.ExpiresAtUtc > time.GetUtcNow()
                ? entry : null;
    }
}
