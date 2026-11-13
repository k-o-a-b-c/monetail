using Monetail.Api.Features.Monetail.Ledgers;

namespace Monetail.Api.Features.Monetail.Receipts;

/// <summary>工程3の保存トランザクション内で取得する、対象家計簿の科目と共通税履歴。</summary>
public sealed record ReceiptMasterData(IReadOnlyDictionary<Guid, IncomeExpenseKind> CategoryIncomeExpenses,
    IReadOnlyList<ReceiptTaxPeriod> TaxPeriods);
public sealed record ReceiptTaxPeriod(DateOnly ValidFrom, DateOnly? ValidTo, decimal? Rate);
