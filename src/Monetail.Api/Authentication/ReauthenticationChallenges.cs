using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace Monetail.Api.Authentication;

/// <summary>本人再確認の検証状態を、開始時のアカウントとセッションに固定する。</summary>
public sealed record ReauthenticationChallenge(string AssertionState, string AuthUserId, byte[] SessionHash,
    DateTimeOffset ExpiresAtUtc);

/// <summary>本人再確認専用の状態を、ブラウザーに結び付けて一度だけ取り出す。</summary>
public sealed class ReauthenticationChallenges(TimeProvider time)
{
    public const string CookieName = "__Host-Monetail.Reauthentication";
    private readonly object gate = new();
    private readonly Dictionary<string, ReauthenticationChallenge> entries = new(StringComparer.Ordinal);

    /// <summary>期限切れと同じブラウザーの旧状態を除き、再確認用の識別子を発行する。</summary>
    public string? Add(ReauthenticationChallenge entry, string? previous)
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

    /// <summary>同時要求も含めて再確認の状態を一度だけ消費し、期限切れを拒否する。</summary>
    public ReauthenticationChallenge? Take(string? id)
    {
        lock (gate)
            return id is not null && entries.Remove(id, out var entry) && entry.ExpiresAtUtc > time.GetUtcNow()
                ? entry : null;
    }
}
