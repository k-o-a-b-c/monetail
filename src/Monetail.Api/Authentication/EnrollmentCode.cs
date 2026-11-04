namespace Monetail.Api.Authentication;

/// <summary>登録リンクのハッシュ・用途・期限・消費状態。既存DBの表・列名を再利用する。</summary>
public sealed class EnrollmentCode
{
    public const string InvitationPurpose = "passkey-invitation";
    public const string InitialAdminPurpose = "passkey-initial-admin";
    public const string RecoveryPurpose = "passkey-recovery";
    public byte[] CodeHash { get; set; } = null!;
    public string AuthUserId { get; set; } = null!;
    public string Purpose { get; set; } = InvitationPurpose;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}

/// <summary>発行時だけ呼出元へ秘密を含むHTTPSリンクを渡す。</summary>
public sealed record IssuedEnrollmentInvitation(string AuthUserId, string EnrollmentUrl, DateTime ExpiresAtUtc)
{
    /// <summary>オブジェクトをログ出力しても生トークンを含めない。</summary>
    public override string ToString() => $"Enrollment link issued for {AuthUserId}";
}
