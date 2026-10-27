using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace Monetail.Api.Authentication;

/// <summary>追加するパスキーの検証状態を、開始時の本人とセッションに固定する。</summary>
public sealed record PasskeyAdditionChallenge(string AuthUserId, byte[] SessionHash,
    string AttestationState, DateTimeOffset ExpiresAtUtc);

/// <summary>パスキー追加専用の状態を、ブラウザーに結び付けて一度だけ消費する。</summary>
public sealed class PasskeyAdditionChallenges(TimeProvider time)
{
    public const string CookieName = "__Host-Monetail.PasskeyAddition";
    private readonly object gate = new();
    private readonly Dictionary<string, PasskeyAdditionChallenge> entries = new(StringComparer.Ordinal);

    /// <summary>期限切れと同じブラウザーの旧状態を除き、追加用の識別子を発行する。</summary>
    public string? Add(PasskeyAdditionChallenge entry, string? previous)
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

    /// <summary>同時要求も含めて追加状態を一度だけ取り出し、期限切れを拒否する。</summary>
    public PasskeyAdditionChallenge? Take(string? id)
    {
        lock (gate)
            return id is not null && entries.Remove(id, out var entry) && entry.ExpiresAtUtc > time.GetUtcNow()
                ? entry : null;
    }
}
