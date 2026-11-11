using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Client.Receipts;

/// <summary>defaultは未選択。非課税と課税0%を分ける。</summary>
public readonly record struct ReceiptTaxSelection
{
    public bool IsSelected { get; }
    public decimal? Rate { get; }
    private ReceiptTaxSelection(bool selected, decimal? rate) { IsSelected = selected; Rate = rate; }
    public static ReceiptTaxSelection Unselected => default;
    public static ReceiptTaxSelection Exempt => new(true, null);
    public static ReceiptTaxSelection Taxed(decimal rate) => new(true, rate);
    internal static ReceiptTaxSelection Stored(decimal? rate) => rate is decimal value ? Taxed(value) : Exempt;
}

/// <summary>呼出側が科目と取引日から解決する。モデルは履歴取得や税率推測をしない。</summary>
public sealed record ReceiptCategoryDefaults(string IncomeExpense, ReceiptTaxSelection Tax);

public sealed class ReceiptHeaderDraft
{
    public DateTime? TransactionAt { get; set; }
    public string ShopName { get; set; } = "";
    public string? ShopLocation { get; set; }
    public Guid? PaymentMethodId { get; set; }
    public int? TaxInclusionMode { get; set; }
    public string? TaxRoundingMode { get; set; }
    public int? RecurrenceInterval { get; set; }
    public string? RecurrenceUnit { get; set; }
    public string? Memo { get; set; }
    internal ReceiptHeaderDraft Copy() => (ReceiptHeaderDraft)MemberwiseClone();
    internal bool Same(ReceiptHeaderDraft other) => TransactionAt == other.TransactionAt && ShopName == other.ShopName && ShopLocation == other.ShopLocation &&
        PaymentMethodId == other.PaymentMethodId && TaxInclusionMode == other.TaxInclusionMode && TaxRoundingMode == other.TaxRoundingMode &&
        RecurrenceInterval == other.RecurrenceInterval && RecurrenceUnit == other.RecurrenceUnit && Memo == other.Memo;
}

/// <summary>入力途中の空欄・不正値を保持する可変フォーム。保存用DTOではない。</summary>
public sealed class ReceiptDetailDraft
{
    public string Name { get; set; } = "";
    public int? UnitPrice { get; set; }
    public int? Quantity { get; set; } = 1;
    public int? UnitDiscountAmount { get; set; } = 0;
    public decimal? DiscountRate { get; set; } = 0;
    public ReceiptTaxSelection Tax { get; set; }
    public string ExpenseType { get; set; } = "";
    public string OccurrenceType { get; set; } = "Irregular";
    public string? Memo { get; set; }
    public List<Guid> PersonIds { get; } = [];
    public ReceiptDetailDraft Copy()
    {
        var copy = new ReceiptDetailDraft { Name = Name, UnitPrice = UnitPrice, Quantity = Quantity, UnitDiscountAmount = UnitDiscountAmount,
            DiscountRate = DiscountRate, Tax = Tax, ExpenseType = ExpenseType, OccurrenceType = OccurrenceType, Memo = Memo };
        copy.PersonIds.AddRange(PersonIds); return copy;
    }
    internal bool Same(ReceiptDetailDraft other) => Name == other.Name && UnitPrice == other.UnitPrice && Quantity == other.Quantity && UnitDiscountAmount == other.UnitDiscountAmount &&
        DiscountRate == other.DiscountRate && Tax == other.Tax && ExpenseType == other.ExpenseType && OccurrenceType == other.OccurrenceType && Memo == other.Memo &&
        PersonIds.Count == other.PersonIds.Count && PersonIds.ToHashSet().SetEquals(other.PersonIds);
    internal ReceiptDetailSnapshot Snapshot(Guid? id, int order, string field)
    {
        static T Required<T>(T? value, string field) where T : struct => value ?? throw new ReceiptInputException(field, "入力してください。");
        if (!Tax.IsSelected) throw new ReceiptInputException(field + ".taxRate", "税率を選択してください。");
        return new(id, order, Name, Required(UnitPrice, field + ".unitPrice"), Required(Quantity, field + ".quantity"),
            Required(UnitDiscountAmount, field + ".unitDiscountAmount"), Required(DiscountRate, field + ".discountRate"), Tax.Rate,
            ExpenseType, OccurrenceType, Memo, PersonIds.ToArray());
    }
    internal static ReceiptDetailDraft From(ReceiptDetailInfo row)
    {
        var result = new ReceiptDetailDraft { Name = row.Name, UnitPrice = row.UnitPrice, Quantity = row.Quantity, UnitDiscountAmount = row.UnitDiscountAmount,
            DiscountRate = row.DiscountRate, Tax = ReceiptTaxSelection.Stored(row.TaxRate), ExpenseType = row.ExpenseType, OccurrenceType = row.OccurrenceType, Memo = row.Memo };
        result.PersonIds.AddRange(row.PersonIds); return result;
    }
}

public sealed class ReceiptEditDetail
{
    public Guid Key { get; } = Guid.NewGuid();
    public Guid? DetailId { get; }
    internal int OriginalOrder { get; }
    internal ReceiptDetailDraft Stored { get; set; }
    public ReceiptDetailDraft Values => Stored.Copy();
    internal ReceiptEditDetail(Guid? id, int order, ReceiptDetailDraft values) { DetailId = id; OriginalOrder = order; Stored = values.Copy(); }
}
public sealed class ReceiptEditGroup
{
    public Guid Key { get; } = Guid.NewGuid();
    public Guid? GroupId { get; }
    public Guid CategoryId { get; }
    public string? Memo { get; internal set; }
    private readonly List<ReceiptEditDetail> rows;
    private readonly Guid[] originalOrder;
    public IReadOnlyList<ReceiptEditDetail> Details { get; }
    internal List<ReceiptEditDetail> Rows => rows;
    internal ReceiptEditGroup(Guid? id, Guid category, string? memo, IEnumerable<ReceiptEditDetail> details)
    {
        GroupId = id; CategoryId = category; Memo = memo; rows = details.ToList(); Details = rows.AsReadOnly(); originalOrder = rows.Select(r => r.Key).ToArray();
    }
    // 表示順が元に戻ったら、元の疎なdisplay_orderも戻して無変更判定を保つ。
    internal bool IsOriginalOrder => rows.Select(r => r.Key).SequenceEqual(originalOrder);
    internal int Order(int index, bool original) => original ? rows[index].OriginalOrder : index;
}
public enum ReceiptSaveKind { Register, Update, ReuseRegistration }
public sealed record ReceiptSavePreparation(ReceiptSaveKind Kind, Guid? ReceiptId, Guid? Revision, ReceiptSnapshot Snapshot);
public enum ReceiptDraftChoice { Stay, ApplyAndMove, DiscardAndMove }
public enum ReceiptTransitionKind { SelectGroup, SelectDetail, AddGroup, RemoveGroup }
public sealed record ReceiptEditTransition(ReceiptTransitionKind Kind, Guid? GroupKey = null, Guid? DetailKey = null, Guid? CategoryId = null);
