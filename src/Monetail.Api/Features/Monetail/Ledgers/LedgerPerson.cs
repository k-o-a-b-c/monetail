namespace Monetail.Api.Features.Monetail.Ledgers;

/// <summary>アカウントから独立した家計簿内の人物を保存する。</summary>
public sealed class LedgerPerson
{
    public string LedgerId { get; set; } = null!;
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public int DisplayOrder { get; set; }
    public string Revision { get; set; } = null!;
}

/// <summary>家計簿参加用の招待と使用・取消状態を保存する。</summary>
public sealed class LedgerInvitation
{
    public string Id { get; set; } = null!;
    public string LedgerId { get; set; } = null!;
    public string Token { get; set; } = null!;
    public string CreatedByAuthUserId { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public string? UsedByAuthUserId { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}
