using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Monetail.Api.Authentication;

namespace Monetail.Api.Features.Monetail.Ledgers;

public sealed record PaymentMethodOrderInfo(string OwnerPersonId, string OrderRevision, string[] PaymentMethodIds);

public sealed partial class LedgerManagementService
{
    public Task<object> SavePaymentOrderAsync(string id, LedgerPayload input, CurrentSession actor, CancellationToken token) =>
        InLedgerAsync(id, actor, PaymentWriter, async _ =>
        {
            var owner = input.Uuid("ownerPersonId");
            await PaymentAccessAsync(id, owner, actor, token);
            var person = await context.Set<LedgerPerson>().AsNoTracking()
                .SingleOrDefaultAsync(row => row.LedgerId == id && row.Id == owner, token) ?? throw Missing();
            var revision = input.Uuid("revision");
            if (person.PaymentOrderRevision != revision) throw Conflict(await PaymentOrderAsync(id, owner, actor, token));
            if (input.Value("paymentMethodIds").ValueKind != JsonValueKind.Array)
                throw LedgerPayload.Bad("支払方法の一覧を指定してください。", "paymentMethodIds");
            var ids = new List<string>();
            foreach (var value in input.Value("paymentMethodIds").EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.String || !Guid.TryParse(value.GetString(), out var key) || key == Guid.Empty)
                    throw LedgerPayload.Bad("支払方法の一覧を確認してください。", "paymentMethodIds");
                ids.Add(key.ToString("N"));
            }
            var rows = await context.Set<LedgerPaymentMethod>().Where(row => row.LedgerId == id && row.OwnerPersonId == owner).ToArrayAsync(token);
            if (ids.Count != rows.Length || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count ||
                !ids.ToHashSet(StringComparer.Ordinal).SetEquals(rows.Select(row => row.Id)))
                throw LedgerPayload.Bad("対象人物の全支払方法を重複なく指定してください。", "paymentMethodIds");
            // 人物編集版・支払方法の行版と独立した、対象所有者の順序版だけを原子的に更新する。
            var count = await context.Set<LedgerPerson>()
                .Where(row => row.LedgerId == id && row.Id == owner && row.PaymentOrderRevision == revision)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.PaymentOrderRevision, Version()), token);
            if (count != 1) throw Conflict(await PaymentOrderAsync(id, owner, actor, token));
            var positions = ids.Select((key, index) => (key, index)).ToDictionary(value => value.key, value => value.index);
            foreach (var row in rows) row.DisplayOrder = positions[row.Id];
            await context.SaveChangesAsync(token);
            var result = await PaymentsAsync(id, actor, token);
            await PaymentAccessAsync(id, owner, actor, token);
            return result;
        }, token);

    private async Task<PaymentMethodOrderInfo> PaymentOrderAsync(string id, string owner, CurrentSession actor, CancellationToken token)
    {
        await PaymentAccessAsync(id, owner, actor, token);
        var revision = await context.Set<LedgerPerson>().AsNoTracking().Where(row => row.LedgerId == id && row.Id == owner)
            .Select(row => row.PaymentOrderRevision).SingleOrDefaultAsync(token) ?? throw Missing();
        var ids = await context.Set<LedgerPaymentMethod>().AsNoTracking().Where(row => row.LedgerId == id && row.OwnerPersonId == owner)
            .OrderBy(row => row.DisplayOrder).ThenBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token);
        await PaymentAccessAsync(id, owner, actor, token);
        return new(owner, revision, ids);
    }
}
