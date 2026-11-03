namespace Monetail.Api.Authentication;

/// <summary>退会や認証リセットから独立して保持する、匿名自己登録の成功日時。</summary>
public sealed class SelfRegistrationCompletion
{
    public long Id { get; set; }
    public DateTime CompletedAtUtc { get; set; }
}
