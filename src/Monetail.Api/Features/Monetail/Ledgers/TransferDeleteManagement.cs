using Microsoft.EntityFrameworkCore;
using Monetail.Api.Authentication;
using Monetail.Api.Features.Monetail.Operations;
using Monetail.Api.Features.Monetail.Transfers;

namespace Monetail.Api.Features.Monetail.Ledgers;

public sealed partial class LedgerManagementService
{
    public Task<object> DeleteTransferAsync(string ledger, string id, LedgerPayload payload, CurrentSession actor, CancellationToken token)
    {
        var owners = new HashSet<string>(StringComparer.Ordinal);
        bool Permission(LedgerMember member) => member.Role == "Admin" || member.Role == "Editor" && owners.All(owner => owner == member.PersonId);
        return InLedgerAsync(ledger, actor, Permission, async _ =>
        {
            var input = TransferDeleteInput.Decode(payload); var hash = OperationRequest.Hash(ledger, id, input.Revision);
            // 成功再送は、削除済み対象の所有権を根拠にせず現在の参加・ロールで確認する。
            if (await PreviousAsync(input.RequestId, "transfer.delete", hash, actor, token) is not null) return new object();
            var row = await context.Set<LedgerTransfer>().AsNoTracking().SingleOrDefaultAsync(row => row.LedgerId == ledger && row.Id == id, token) ?? throw Missing();
            await ReceiptPaymentAccessAsync(ledger, row.FromPaymentMethodId, owners, actor, token);
            await ReceiptPaymentAccessAsync(ledger, row.ToPaymentMethodId, owners, actor, token);
            if (row.Revision != input.Revision) throw await TransferConflictAsync(ledger, id, owners, actor, token);
            var count = await context.Set<LedgerTransfer>().Where(row => row.LedgerId == ledger && row.Id == id && row.Revision == input.Revision).ExecuteDeleteAsync(token);
            if (count != 1) throw await TransferConflictAsync(ledger, id, owners, actor, token);
            Record(input.RequestId, "transfer.delete", hash, id, actor); await context.SaveChangesAsync(token);
            // 初回削除では成功記録INSERT後も両側の現在の所有権を照合し、確定前の変化を戻す。
            await ReceiptPaymentAccessAsync(ledger, row.FromPaymentMethodId, owners, actor, token);
            await ReceiptPaymentAccessAsync(ledger, row.ToPaymentMethodId, owners, actor, token);
            return new object();
        }, token);
    }
}
