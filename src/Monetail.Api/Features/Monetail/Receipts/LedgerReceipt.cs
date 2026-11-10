namespace Monetail.Api.Features.Monetail.Receipts;

/// <summary>子のグループ・明細・対象人物を含む一括保存単位。取引日時はローカル日時。</summary>
public sealed class LedgerReceipt
{
    public string LedgerId { get; set; } = null!;
    public string Id { get; set; } = null!;
    public DateTime TransactionAt { get; set; }
    public string ShopName { get; set; } = null!;
    public string? ShopLocation { get; set; }
    public string PaymentMethodId { get; set; } = null!;
    public int TaxInclusionMode { get; set; }
    public string TaxRoundingMode { get; set; } = null!;
    public int? RecurrenceInterval { get; set; }
    public string? RecurrenceUnit { get; set; }
    public int TotalAmount { get; set; }
    public string? Memo { get; set; }
    public string Revision { get; set; } = null!;
    public string CreatedByAuthUserId { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public string UpdatedByAuthUserId { get; set; } = null!;
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>一つのレシート内の一つの科目。税率・独自の表示順・更新版は持たない。</summary>
public sealed class LedgerReceiptGroup
{
    public string LedgerId { get; set; } = null!;
    public string Id { get; set; } = null!;
    public string ReceiptId { get; set; } = null!;
    public string CategoryId { get; set; } = null!;
    public string? Memo { get; set; }
}

/// <summary>実税率と費用・発生区分を保持する明細。DecimalはTEXTで保存する。</summary>
public sealed class LedgerReceiptDetail
{
    public string LedgerId { get; set; } = null!;
    public string Id { get; set; } = null!;
    public string GroupId { get; set; } = null!;
    public int DisplayOrder { get; set; }
    public string Name { get; set; } = null!;
    public int UnitPrice { get; set; }
    public int Quantity { get; set; }
    public int UnitDiscountAmount { get; set; }
    public decimal DiscountRate { get; set; }
    public decimal? TaxRate { get; set; }
    public string ExpenseType { get; set; } = null!;
    public string OccurrenceType { get; set; } = null!;
    public string? Memo { get; set; }
}

/// <summary>明細の対象人物。認証アカウントとは別の人物マスタを参照する。</summary>
public sealed class LedgerReceiptDetailPerson
{
    public string LedgerId { get; set; } = null!;
    public string DetailId { get; set; } = null!;
    public string PersonId { get; set; } = null!;
}
