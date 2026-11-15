using Microsoft.EntityFrameworkCore;
using Monetail.Api.Authentication;
using Monetail.Api.Features.Monetail.Operations;
using Monetail.Api.Features.Monetail.Transfers;

namespace Monetail.Api.Features.Monetail.Ledgers;

public sealed partial class LedgerManagementService
{
    public Task<object> CreateTransferAsync(string ledger, LedgerPayload payload, CurrentSession actor, CancellationToken token)
    {
        var owners = new HashSet<string>(StringComparer.Ordinal);
        bool Permission(LedgerMember member) => member.Role == "Admin" || member.Role == "Editor" && owners.All(owner => owner == member.PersonId);
        return InLedgerAsync(ledger, actor, Permission, async _ =>
        {
            var input = TransferCreateInput.Decode(payload); var hash = OperationRequest.Hash(ledger, input.Canonical());
            var previous = await PreviousAsync(input.RequestId, "transfer.create", hash, actor, token);
            await ReceiptPaymentAccessAsync(ledger, input.FromPaymentMethodId, owners, actor, token);
            await ReceiptPaymentAccessAsync(ledger, input.ToPaymentMethodId, owners, actor, token);
            // 再送も現在の所有権で照合し、記録済みの成功とLocationだけを再現する。
            if (previous is not null) return new TransferCreatedResult(previous.ResultId);
            await ValidateTransferPeriodsAsync(ledger, input, token);
            await RequireMemberAsync(ledger, actor, Permission, token);
            var id = Guid.CreateVersion7().ToString("N"); var now = Now;
            context.Add(new LedgerTransfer
            {
                LedgerId = ledger, Id = id, TransactionAt = input.TransactionAt, Name = input.Name,
                FromPaymentMethodId = input.FromPaymentMethodId, ToPaymentMethodId = input.ToPaymentMethodId,
                Amount = input.Amount, Memo = input.Memo, Revision = Version(),
                CreatedByAuthUserId = actor.Session.AuthUserId, UpdatedByAuthUserId = actor.Session.AuthUserId,
                CreatedAtUtc = now, UpdatedAtUtc = now
            });
            await context.SaveChangesAsync(token);
            Record(input.RequestId, "transfer.create", hash, id, actor); await context.SaveChangesAsync(token);
            // 記録INSERTによる変化も含め、確定前に最新の本人対応・所有者・期間を取り直す。
            await ReceiptPaymentAccessAsync(ledger, input.FromPaymentMethodId, owners, actor, token);
            await ReceiptPaymentAccessAsync(ledger, input.ToPaymentMethodId, owners, actor, token);
            await ValidateTransferPeriodsAsync(ledger, input, token);
            return new TransferCreatedResult(id);
        }, token);
    }

    private async Task ValidateTransferPeriodsAsync(string ledger, TransferCreateInput input, CancellationToken token)
    {
        var day = DateOnly.FromDateTime(input.TransactionAt);
        foreach (var (id, field) in new[] { (input.FromPaymentMethodId, "fromPaymentMethodId"), (input.ToPaymentMethodId, "toPaymentMethodId") })
        {
            var payment = await context.Set<LedgerPaymentMethod>().AsNoTracking().SingleOrDefaultAsync(row => row.LedgerId == ledger && row.Id == id, token) ?? throw Missing();
            var person = await context.Set<LedgerPerson>().AsNoTracking().SingleOrDefaultAsync(row => row.LedgerId == ledger && row.Id == payment.OwnerPersonId, token) ?? throw Missing();
            if (!InPeriod(day, payment.ValidFrom, payment.ValidTo) || !InPeriod(day, person.ValidFrom, person.ValidTo))
                throw LedgerPayload.Bad("取引日に有効な支払方法・所有人物を指定してください。", field);
        }
    }
}
