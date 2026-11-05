namespace Monetail.Api.Features.Monetail.Ledgers;

/// <summary>家計簿の人物一人に所有される支払方法。所有者は作成後変更しない。</summary>
public sealed class LedgerPaymentMethod
{
    public string LedgerId { get; set; } = null!;
    public string Id { get; set; } = null!;
    public string OwnerPersonId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public int DisplayOrder { get; set; }
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public string Revision { get; set; } = null!;
}
