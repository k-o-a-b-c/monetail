namespace Monetail.Api.Authentication;

/// <summary>ログインセッションの対象と固定UTC期限・失効状態を保存する。</summary>
public sealed class AuthSession
{
    public byte[] IdHash { get; set; } = null!;
    public string AuthUserId { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime VerifiedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}
