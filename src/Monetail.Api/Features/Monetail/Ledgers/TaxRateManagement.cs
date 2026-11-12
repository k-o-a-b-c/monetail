using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Monetail.Api.Authentication;
using Monetail.Api.Features.Monetail.Taxes;

namespace Monetail.Api.Features.Monetail.Ledgers;

public sealed record TaxRateInfo(string TaxCategoryCode, DateOnly ValidFrom, DateOnly? ValidTo, string? Rate);
public sealed record TaxRatesResponse(TaxRateInfo[] Rates);

public sealed partial class LedgerManagementService
{
    // データは全家計簿共通。指定家計簿の参加・認証は既存の境界で取得前後に照合する。
    public Task<object> GetTaxRatesAsync(string ledger, CurrentSession actor, CancellationToken token) =>
        InLedgerAsync(ledger, actor, _ => true, async _ =>
        {
            var rows = await context.Set<TaxRateHistory>().AsNoTracking().ToArrayAsync(token);
            return new TaxRatesResponse(rows.Select(row => new TaxRateInfo(row.TaxCategoryCode, row.ValidFrom,
                row.ValidTo, row.Rate?.ToString(CultureInfo.InvariantCulture))).ToArray());
        }, token);
}
