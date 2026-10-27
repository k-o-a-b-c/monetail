namespace Monetail.Api.Authentication;

/// <summary>初回アカウント発行の要求と結果を保存し、再送による二重作成を防ぐ。</summary>
public sealed class AccountCreationRequest
{
    public string RequestId { get; set; } = null!;
    public string CreatedByAuthUserId { get; set; } = null!;
    public string AuthUserId { get; set; } = null!;
    /// <summary>旧版で発行時に指定した人物コードを履歴として保持し、新規発行では設定しない。</summary>
    public int? UserCode { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
