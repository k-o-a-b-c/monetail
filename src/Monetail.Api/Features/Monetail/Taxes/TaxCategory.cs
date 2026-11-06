namespace Monetail.Api.Features.Monetail.Taxes;

/// <summary>全家計簿で共有する税区分。食品と新聞は同じ税率でも別コードで保持する。</summary>
public sealed class TaxCategory
{
    public const string Standard = "Standard";
    public const string ReducedFood = "ReducedFood";
    public const string ReducedNewspaper = "ReducedNewspaper";
    public const string Exempt = "Exempt";
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
}

/// <summary>両端を含む期間と、その期間の税率（百分率）。保存済み取引の参照先にはしない。</summary>
public sealed class TaxRateHistory
{
    public string Id { get; set; } = null!;
    public string TaxCategoryCode { get; set; } = null!;
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    // DecimalをSQLiteのTEXTに保存し、REALへの変換による丸めを避ける。非課税はNULL。
    public decimal? Rate { get; set; }
    public string Revision { get; set; } = null!;
}
