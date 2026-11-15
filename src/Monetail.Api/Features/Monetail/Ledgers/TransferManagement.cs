using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Monetail.Api.Authentication;
using Monetail.Api.Features.Monetail.Transfers;

namespace Monetail.Api.Features.Monetail.Ledgers;

public sealed partial class LedgerManagementService
{
    public Task<object> GetTransferAsync(string ledger, string id, CurrentSession actor, CancellationToken token) =>
        InLedgerAsync(ledger, actor, _ => true, async _ =>
        {
            var row = await context.Set<LedgerTransfer>().AsNoTracking()
                .SingleOrDefaultAsync(row => row.LedgerId == ledger && row.Id == id, token) ?? throw Missing();
            return new TransferInfo(D(row.Id), row.TransactionAt.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
                row.Name, D(row.FromPaymentMethodId), D(row.ToPaymentMethodId), row.Amount,
                row.Memo == "" ? null : row.Memo, D(row.Revision));
        }, token);
}
