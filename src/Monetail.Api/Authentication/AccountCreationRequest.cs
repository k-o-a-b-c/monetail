namespace Monetail.Api.Authentication;

/// <summary>初回アカウント発行の要求と結果を保存し、再送による二重作成を防ぐ。</summary>
public sealed class AccountCreationRequest
{
    public string RequestId { get; set; } = null!;
    public string CreatedByAuthUserId { get; set; } = null!;
    public string AuthUserId { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
}
