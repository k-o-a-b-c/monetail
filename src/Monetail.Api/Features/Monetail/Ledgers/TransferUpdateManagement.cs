using Microsoft.EntityFrameworkCore;
using Monetail.Api.Authentication;
using Monetail.Api.Features.Monetail.Operations;
using Monetail.Api.Features.Monetail.Receipts;
using Monetail.Api.Features.Monetail.Transfers;

namespace Monetail.Api.Features.Monetail.Ledgers;

public sealed partial class LedgerManagementService
{
    public Task<object> UpdateTransferAsync(string ledger, string id, LedgerPayload payload, CurrentSession actor, CancellationToken token)
    {
        var owners = new HashSet<string>(StringComparer.Ordinal);
        bool Permission(LedgerMember member) => member.Role == "Admin" || member.Role == "Editor" && owners.All(owner => owner == member.PersonId);
        async Task Access(string from, string to)
        {
            await ReceiptPaymentAccessAsync(ledger, from, owners, actor, token);
            await ReceiptPaymentAccessAsync(ledger, to, owners, actor, token);
        }
        return InLedgerAsync(ledger, actor, Permission, async _ =>
        {
            var update = TransferUpdateInput.Decode(payload); var input = update.Values;
            var hash = OperationRequest.Hash(ledger, id, update.Revision, input.Canonical());
            var replay = await PreviousAsync(input.RequestId, "transfer.update", hash, actor, token);
            var old = await context.Set<LedgerTransfer>().AsNoTracking().SingleOrDefaultAsync(row => row.LedgerId == ledger && row.Id == id, token);
            if (old is null && replay is null) throw Missing();
            if (old is not null) await Access(old.FromPaymentMethodId, old.ToPaymentMethodId);
            await Access(input.FromPaymentMethodId, input.ToPaymentMethodId);
            if (replay is not null) return new object();
            if (old!.Revision != update.Revision) throw await TransferUpdateConflictAsync(ledger, id, owners, actor, token);
            await ValidateTransferPeriodsAsync(ledger, input, token);
            await RequireMemberAsync(ledger, actor, Permission, token);
            var changed = old.TransactionAt != input.TransactionAt || old.Name != input.Name ||
                old.FromPaymentMethodId != input.FromPaymentMethodId || old.ToPaymentMethodId != input.ToPaymentMethodId ||
                old.Amount != input.Amount || ReceiptPayload.NormalizeMemo(old.Memo) != input.Memo;
            if (changed)
            {
                var revision = Version(); var now = Now;
                var count = await context.Set<LedgerTransfer>().Where(row => row.LedgerId == ledger && row.Id == id && row.Revision == update.Revision)
                    .ExecuteUpdateAsync(set => set.SetProperty(row => row.TransactionAt, input.TransactionAt).SetProperty(row => row.Name, input.Name)
                        .SetProperty(row => row.FromPaymentMethodId, input.FromPaymentMethodId).SetProperty(row => row.ToPaymentMethodId, input.ToPaymentMethodId)
                        .SetProperty(row => row.Amount, input.Amount).SetProperty(row => row.Memo, input.Memo).SetProperty(row => row.Revision, revision)
                        .SetProperty(row => row.UpdatedByAuthUserId, actor.Session.AuthUserId).SetProperty(row => row.UpdatedAtUtc, now), token);
                if (count != 1) throw await TransferUpdateConflictAsync(ledger, id, owners, actor, token);
                Record(input.RequestId, "transfer.update", hash, id, actor); await context.SaveChangesAsync(token);
            }
            else
            {
                // 行をUPDATEせず、保存済み版が一致するときだけ成功記録を挿入する。
                var count = await context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO operation_requests(request_id,operation_type,actor_auth_user_id,request_hash,result_id,created_at_utc)
                    SELECT {input.RequestId},{"transfer.update"},{actor.Session.AuthUserId},{hash},{id},{Now}
                    WHERE EXISTS(SELECT 1 FROM ledger_transfers WHERE ledger_id={ledger} AND transfer_id={id} AND revision={update.Revision})
                    """, token);
                if (count != 1) throw await TransferUpdateConflictAsync(ledger, id, owners, actor, token);
            }
            await Access(old.FromPaymentMethodId, old.ToPaymentMethodId);
            await Access(input.FromPaymentMethodId, input.ToPaymentMethodId);
            await ValidateTransferPeriodsAsync(ledger, input, token);
            return new object();
        }, token);
    }

    private async Task<LedgerApiFault> TransferUpdateConflictAsync(string ledger, string id, HashSet<string> owners, CurrentSession actor, CancellationToken token)
    {
        var latest = await context.Set<LedgerTransfer>().AsNoTracking().SingleOrDefaultAsync(row => row.LedgerId == ledger && row.Id == id, token) ?? throw Missing();
        await ReceiptPaymentAccessAsync(ledger, latest.FromPaymentMethodId, owners, actor, token);
        await ReceiptPaymentAccessAsync(ledger, latest.ToPaymentMethodId, owners, actor, token);
        return Conflict(TransferDetails(latest));
    }
}
