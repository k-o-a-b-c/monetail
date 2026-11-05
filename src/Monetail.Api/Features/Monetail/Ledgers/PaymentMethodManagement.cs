using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Monetail.Api.Authentication;
using Monetail.Api.Features.Monetail.Operations;

namespace Monetail.Api.Features.Monetail.Ledgers;

public sealed record PaymentMethodInfo(string PaymentMethodId, string OwnerPersonId, string Name, int DisplayOrder, DateOnly? ValidFrom, DateOnly? ValidTo, string Revision);
public sealed record PaymentOwnerOrder(string PersonId, string OrderRevision);
public sealed record PaymentMethodsResponse(PaymentMethodInfo[] PaymentMethods, PaymentOwnerOrder[] OwnerOrderRevisions, string? MyPersonId, string CurrentRole);

public sealed partial class LedgerManagementService
{
    private static bool PaymentWriter(LedgerMember member) => member.Role is "Admin" or "Editor";
    private Task<LedgerMember> PaymentAccessAsync(string id, string owner, CurrentSession actor, CancellationToken token) =>
        RequireMemberAsync(id, actor, member => member.Role == "Admin" || member.Role == "Editor" && member.PersonId == owner, token);
    private static PaymentMethodInfo PaymentInfo(LedgerPaymentMethod row) => new(row.Id, row.OwnerPersonId, row.Name, row.DisplayOrder, row.ValidFrom, row.ValidTo, row.Revision);
    private async Task<PaymentMethodsResponse> PaymentsAsync(string id, CurrentSession actor, CancellationToken token)
    {
        var member = await RequireMemberAsync(id, actor, _ => true, token);
        var owners = await context.Set<LedgerPerson>().AsNoTracking().Where(row => row.LedgerId == id).OrderBy(row => row.DisplayOrder).ThenBy(row => row.Id)
            .Select(row => new PaymentOwnerOrder(row.Id, row.PaymentOrderRevision)).ToArrayAsync(token);
        var rows = await context.Set<LedgerPaymentMethod>().AsNoTracking().Where(row => row.LedgerId == id)
            .OrderBy(row => row.OwnerPersonId).ThenBy(row => row.DisplayOrder).ThenBy(row => row.Id).ToArrayAsync(token);
        return new(rows.Select(PaymentInfo).ToArray(), owners, member.PersonId, member.Role);
    }
    public Task<object> GetPaymentMethodsAsync(string id, CurrentSession actor, CancellationToken token) =>
        InLedgerAsync(id, actor, _ => true, async _ => await PaymentsAsync(id, actor, token), token);

    public Task<object> CreatePaymentMethodAsync(string id, LedgerPayload input, CurrentSession actor, CancellationToken token) =>
        InLedgerAsync(id, actor, PaymentWriter, async _ =>
        {
            var owner = input.Uuid("ownerPersonId");
            var person = await context.Set<LedgerPerson>().AsNoTracking().SingleOrDefaultAsync(row => row.LedgerId == id && row.Id == owner, token)
                ?? throw LedgerPayload.Bad("同じ家計簿の所有者を指定してください。", "ownerPersonId");
            await PaymentAccessAsync(id, owner, actor, token);
            var name = PaymentName(input.Text("name")); var from = input.Date("validFrom"); var to = input.Date("validTo"); Period(from, to);
            var request = input.Uuid("requestId");
            var hash = OperationRequest.Hash(id, owner, name, from?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), to?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            var previous = await PreviousAsync(request, "payment_method.create", hash, actor, token);
            if (previous is not null)
            {
                if (!await context.Set<LedgerPaymentMethod>().AnyAsync(row => row.LedgerId == id && row.Id == previous.ResultId, token))
                    throw new LedgerApiFault(409, "created_result_deleted", "作成した支払方法は削除されています。");
                var replay = await PaymentsAsync(id, actor, token); await PaymentAccessAsync(id, owner, actor, token);
                return new LedgerCreatedBody(new { paymentMethodId = previous.ResultId, replay.PaymentMethods, replay.OwnerOrderRevisions, replay.MyPersonId, replay.CurrentRole }, true);
            }
            await UniquePaymentNameAsync(id, owner, null, name, token);
            await AdvancePaymentOrderAsync(person, actor, token);
            var last = await context.Set<LedgerPaymentMethod>().Where(row => row.LedgerId == id && row.OwnerPersonId == owner).MaxAsync(row => (int?)row.DisplayOrder, token);
            if (last == int.MaxValue) throw LedgerPayload.Bad("表示順の上限に達しています。");
            var row = new LedgerPaymentMethod { LedgerId = id, Id = Version(), OwnerPersonId = owner, Name = name, ValidFrom = from, ValidTo = to, DisplayOrder = (last ?? -1) + 1, Revision = Version() };
            context.Add(row); Record(request, "payment_method.create", hash, row.Id, actor);
            try { await context.SaveChangesAsync(token); }
            catch (DbUpdateException error) when (PaymentNameCollision(error)) { throw DuplicatePaymentName(); }
            var result = await PaymentsAsync(id, actor, token); await PaymentAccessAsync(id, owner, actor, token);
            return new LedgerCreatedBody(new { paymentMethodId = row.Id, result.PaymentMethods, result.OwnerOrderRevisions, result.MyPersonId, result.CurrentRole }, false);
        }, token);

    public Task<object> UpdatePaymentMethodAsync(string id, string key, LedgerPayload input, CurrentSession actor, CancellationToken token) =>
        InLedgerAsync(id, actor, PaymentWriter, async _ =>
        {
            var row = await PaymentRowAsync(id, key, token); await PaymentAccessAsync(id, row.OwnerPersonId, actor, token);
            var revision = input.Uuid("revision");
            if (!input.Has("name") && !input.Has("validFrom") && !input.Has("validTo")) throw LedgerPayload.Bad("変更項目を指定してください。");
            if (row.Revision != revision) throw Conflict(PaymentInfo(row));
            var name = input.Has("name") ? PaymentName(input.Text("name")) : row.Name;
            var from = input.Has("validFrom") ? input.Date("validFrom") : row.ValidFrom;
            var to = input.Has("validTo") ? input.Date("validTo") : row.ValidTo; Period(from, to);
            if (name != row.Name || from != row.ValidFrom || to != row.ValidTo)
            {
                await UniquePaymentNameAsync(id, row.OwnerPersonId, key, name, token);
                try
                {
                    var count = await context.Set<LedgerPaymentMethod>().Where(p => p.LedgerId == id && p.Id == key && p.Revision == revision)
                        .ExecuteUpdateAsync(set => set.SetProperty(p => p.Name, name).SetProperty(p => p.ValidFrom, from).SetProperty(p => p.ValidTo, to).SetProperty(p => p.Revision, Version()), token);
                    if (count != 1) throw Conflict(PaymentInfo(await PaymentRowAsync(id, key, token)));
                }
                catch (SqliteException error) when (PaymentNameCollision(error)) { throw DuplicatePaymentName(); }
            }
            // 実値不変時は書き込まない。InLedgerAsyncのSQLite書込トランザクション内で旧版を照合済み。
            var result = await PaymentsAsync(id, actor, token); await PaymentAccessAsync(id, row.OwnerPersonId, actor, token); return result;
        }, token);

    public Task<object> DeletePaymentMethodAsync(string id, string key, LedgerPayload input, CurrentSession actor, CancellationToken token) =>
        InLedgerAsync(id, actor, PaymentWriter, async _ =>
        {
            var row = await PaymentRowAsync(id, key, token); await PaymentAccessAsync(id, row.OwnerPersonId, actor, token);
            var revision = input.Uuid("revision");
            if (row.Revision != revision) throw Conflict(PaymentInfo(row));
            var owner = await context.Set<LedgerPerson>().AsNoTracking().SingleAsync(p => p.LedgerId == id && p.Id == row.OwnerPersonId, token);
            // 将来の業務表は作らない。参照表追加後も実際のRESTRICT違反をここで変換する。
            try
            {
                var count = await context.Set<LedgerPaymentMethod>().Where(p => p.LedgerId == id && p.Id == key && p.Revision == revision).ExecuteDeleteAsync(token);
                if (count != 1) throw Conflict(PaymentInfo(await PaymentRowAsync(id, key, token)));
            }
            catch (SqliteException error) when (error.SqliteExtendedErrorCode == 787 || error.SqliteExtendedErrorCode == 1811 && error.Message.Contains("FOREIGN KEY constraint failed", StringComparison.Ordinal))
            { throw new LedgerApiFault(409, "payment_method_referenced", "この支払方法は参照されているため削除できません。"); }
            await AdvancePaymentOrderAsync(owner, actor, token);
            var result = await PaymentsAsync(id, actor, token); await PaymentAccessAsync(id, row.OwnerPersonId, actor, token); return result;
        }, token);
    private async Task<LedgerPaymentMethod> PaymentRowAsync(string id, string key, CancellationToken token) =>
        await context.Set<LedgerPaymentMethod>().AsNoTracking().SingleOrDefaultAsync(row => row.LedgerId == id && row.Id == key, token) ?? throw Missing();
    private async Task AdvancePaymentOrderAsync(LedgerPerson person, CurrentSession actor, CancellationToken token)
    {
        var count = await context.Set<LedgerPerson>().Where(row => row.LedgerId == person.LedgerId && row.Id == person.Id && row.PaymentOrderRevision == person.PaymentOrderRevision)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.PaymentOrderRevision, Version()), token);
        if (count != 1) throw Conflict(await PaymentsAsync(person.LedgerId, actor, token));
    }
    private static string PaymentName(string value)
    {
        var name = value.Trim();
        if (name.Length is < 1 or > 100) throw LedgerPayload.Bad("支払方法名は1〜100文字で入力してください。", "name");
        return name;
    }
    private static LedgerApiFault DuplicatePaymentName() => new(409, "duplicate_payment_method_name", "同じ所有者に同名の支払方法があります。", "name");
    private async Task UniquePaymentNameAsync(string id, string owner, string? key, string name, CancellationToken token)
    {
        if (await context.Set<LedgerPaymentMethod>().AnyAsync(row => row.LedgerId == id && row.OwnerPersonId == owner && row.Id != key && row.Name == name, token)) throw DuplicatePaymentName();
    }
    private static bool PaymentNameCollision(Exception error) => (error is DbUpdateException ? error.InnerException : error) is SqliteException sqlite && sqlite.SqliteExtendedErrorCode == 2067 && sqlite.Message.Contains("ledger_payment_methods.ledger_id, ledger_payment_methods.owner_person_id, ledger_payment_methods.name", StringComparison.Ordinal);
}
