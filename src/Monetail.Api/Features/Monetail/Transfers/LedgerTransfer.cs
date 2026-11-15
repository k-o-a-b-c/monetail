namespace Monetail.Api.Features.Monetail.Transfers;

/// <summary>同一家計簿の二つの支払方法間の資金移動。採番・版照合・監査値の設定は保存APIの責務。</summary>
public sealed class LedgerTransfer
{
    public string LedgerId { get; set; } = null!;
    public string Id { get; set; } = null!;
    public DateTime TransactionAt { get; set; }
    public string Name { get; set; } = null!;
    public string FromPaymentMethodId { get; set; } = null!;
    public string ToPaymentMethodId { get; set; } = null!;
    public int Amount { get; set; }
    public string? Memo { get; set; }
    public string Revision { get; set; } = null!;
    public string CreatedByAuthUserId { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public string UpdatedByAuthUserId { get; set; } = null!;
    public DateTime UpdatedAtUtc { get; set; }
}
