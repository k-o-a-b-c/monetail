namespace Monetail.Api.Authentication;

/// <summary>パスキー登録コードのハッシュと、有効期限・消費・失効状態を保存する。</summary>
public sealed class EnrollmentCode
{
    public byte[] CodeHash { get; set; } = null!;
    public string AuthUserId { get; set; } = null!;
    public string Purpose { get; set; } = "passkey-enrollment";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}

/// <summary>発行時だけ呼出元へ生コードを渡す。</summary>
public sealed record IssuedEnrollmentCode(string AuthUserId, string Code, DateTime ExpiresAtUtc)
{
    /// <summary>オブジェクトをログ出力しても生コードを含めない。</summary>
    public override string ToString() => $"Enrollment code issued for {AuthUserId}";
}
