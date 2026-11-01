using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Monetail.Api.Authentication;

namespace Monetail.Api.Features.Monetail.Ledgers;

public sealed partial class LedgerManagementService
{
    public Task<object> GetMembersAsync(string id, CurrentSession actor, CancellationToken token) =>
        InLedgerAsync(id, actor, member => member.Role == "Admin", async _ => await MembersAsync(id, actor, token), token);

    public Task<object> GetMemberEditAsync(string id, string accountId, CurrentSession actor, CancellationToken token) =>
        InLedgerAsync(id, actor, member => member.Role == "Admin", async _ => await MemberEditAsync(id, accountId, token), token);

    private async Task<MemberEditResponse> MemberEditAsync(string id, string accountId, CancellationToken token)
    {
        var member = await MemberRows(id, accountId).SingleOrDefaultAsync(token) ?? throw Missing();
        var assigned = await context.Set<LedgerMember>().Where(row => row.LedgerId == id && row.AuthUserId != accountId && row.PersonId != null)
            .Select(row => row.PersonId!).ToArrayAsync(token);
        var people = await context.Set<LedgerPerson>().AsNoTracking().Where(row => row.LedgerId == id)
            .OrderBy(row => row.DisplayOrder).ThenBy(row => row.Id).Select(row => new { row.Id, row.Name }).ToArrayAsync(token);
        return new(member, people.Select(row => new PersonCandidate(row.Id, row.Name, !assigned.Contains(row.Id, StringComparer.Ordinal))).ToArray());
    }

    public Task<object> UpdateMemberAsync(string id, string accountId, LedgerPayload input, CurrentSession actor, CancellationToken token) =>
        InLedgerAsync(id, actor, member => member.Role == "Admin", async _ =>
        {
            var member = await context.Set<LedgerMember>().SingleOrDefaultAsync(row => row.LedgerId == id && row.AuthUserId == accountId, token) ?? throw Missing();
            if (member.Revision != input.Uuid("revision")) throw Conflict(await MemberEditAsync(id, accountId, token));
            var role = input.Has("role") ? input.Text("role") : member.Role;
            if (role is not ("Viewer" or "Editor" or "Admin")) throw LedgerPayload.Bad("役割を確認してください。", "role");
            if (accountId == actor.Session.AuthUserId && role != member.Role)
                throw new LedgerApiFault(403, "self_role_change", "自分の役割は変更できません。別の管理者に依頼してください。");
            var personId = !input.Has("personId") ? member.PersonId : input.Value("personId").ValueKind == JsonValueKind.Null ? null : input.Uuid("personId");
            if (personId is not null)
            {
                if (!await context.Set<LedgerPerson>().AnyAsync(row => row.LedgerId == id && row.Id == personId, token)) throw Missing();
                if (await context.Set<LedgerMember>().AnyAsync(row => row.LedgerId == id && row.AuthUserId != accountId && row.PersonId == personId, token))
                    throw new LedgerApiFault(409, "person_already_assigned", "この人物は別の参加者に設定されています。", "personId");
            }
            if (member.Role != role || member.PersonId != personId)
            {
                member.Role = role; member.PersonId = personId; member.Revision = Version();
                await context.SaveChangesAsync(token);
            }
            return await MembersAsync(id, actor, token);
        }, token);

    public Task<object> ExcludeMemberAsync(string id, string accountId, LedgerPayload input, CurrentSession actor, CancellationToken token) =>
        InLedgerAsync(id, actor, member => member.Role == "Admin", async _ =>
        {
            if (accountId == actor.Session.AuthUserId)
                throw new LedgerApiFault(403, "self_exclusion", "自分を除外できません。別の管理者に依頼してください。");
            var member = await context.Set<LedgerMember>().SingleOrDefaultAsync(row => row.LedgerId == id && row.AuthUserId == accountId, token) ?? throw Missing();
            if (member.Revision != input.Uuid("revision")) throw Conflict(await MemberEditAsync(id, accountId, token));
            context.Remove(member);
            await context.SaveChangesAsync(token);
            return await MembersAsync(id, actor, token);
        }, token);
}
