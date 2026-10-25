using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace Monetail.Api.Authentication;

/// <summary>ログインの検証状態と期限計算用地域を、一時的に保持する。</summary>
public sealed record LoginChallenge(string AssertionState, TimeZoneInfo Zone, DateTimeOffset ExpiresAtUtc);

/// <summary>ログイン専用チャレンジをブラウザーに結び付け、一度だけ取り出す。</summary>
public sealed class LoginChallenges(TimeProvider time)
{
    public const string CookieName = "__Host-Monetail.Login";
    private readonly object gate = new();
    private readonly Dictionary<string, LoginChallenge> entries = new(StringComparer.Ordinal);

    /// <summary>期限切れと同じブラウザーの旧状態を除き、5分の識別子を発行する。</summary>
    public string? Add(LoginChallenge entry, string? previous)
    {
        lock (gate)
        {
            foreach (var key in entries.Where(pair => pair.Value.ExpiresAtUtc <= time.GetUtcNow())
                .Select(pair => pair.Key).ToArray()) entries.Remove(key);
            if (previous is not null) entries.Remove(previous);
            if (entries.Count >= 1024) return null;
            var id = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            entries.Add(id, entry);
            return id;
        }
    }

    /// <summary>同時要求も含めてチャレンジを一度だけ消費し、期限切れを拒否する。</summary>
    public LoginChallenge? Take(string? id)
    {
        lock (gate)
            return id is not null && entries.Remove(id, out var entry) && entry.ExpiresAtUtc > time.GetUtcNow()
                ? entry : null;
    }
}
