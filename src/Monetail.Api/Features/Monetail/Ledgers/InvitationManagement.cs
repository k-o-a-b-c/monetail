using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Monetail.Api.Authentication;
using Monetail.Api.Features.Monetail.Operations;

namespace Monetail.Api.Features.Monetail.Ledgers;

public sealed record InvitationInfo(string InvitationId, string Url, DateTime CreatedAtUtc, DateTime ExpiresAtUtc);
public sealed record InvitationsResponse(InvitationInfo[] Invitations);
public sealed record InvitationPreview(string Status, string LedgerId, string LedgerName, DateTime ExpiresAtUtc);

public sealed partial class LedgerManagementService
{
    public Task<object> GetInvitationsAsync(string id, CurrentSession actor, CancellationToken token) =>
        InLedgerAsync(id, actor, member => member.Role == "Admin", async _ => await InvitationsAsync(id, token), token);

    private InvitationInfo Invitation(LedgerInvitation row) => new(row.Id,
        settings.Origin.GetLeftPart(UriPartial.Authority) + "/ledger-invitation#token=" + row.Token,
        DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc), DateTime.SpecifyKind(row.ExpiresAtUtc, DateTimeKind.Utc));

    private async Task<InvitationsResponse> InvitationsAsync(string id, CancellationToken token)
    {
        var now = Now;
        var rows = await context.Set<LedgerInvitation>().AsNoTracking().Where(row => row.LedgerId == id &&
            row.UsedAtUtc == null && row.RevokedAtUtc == null && row.ExpiresAtUtc > now)
            .OrderBy(row => row.CreatedAtUtc).ThenBy(row => row.Id).ToArrayAsync(token);
        return new(rows.Select(Invitation).ToArray());
    }

    public Task<object> CreateInvitationAsync(string id, LedgerPayload input, CurrentSession actor, CancellationToken token) =>
        InLedgerAsync(id, actor, member => member.Role == "Admin", async _ =>
        {
            var requestId = input.Uuid("requestId");
            var hash = OperationRequest.Hash(id);
            var previous = await PreviousAsync(requestId, "ledger-invitation.create", hash, actor, token);
            LedgerInvitation invite;
            if (previous is not null)
            {
                invite = await context.Set<LedgerInvitation>().AsNoTracking().SingleOrDefaultAsync(
                    row => row.Id == previous.ResultId && row.LedgerId == id, token) ?? throw Missing();
                InvitationActive(invite);
            }
            else
            {
                var now = Now;
                invite = new LedgerInvitation
                {
                    Id = Version(), LedgerId = id, Token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32)),
                    CreatedByAuthUserId = actor.Session.AuthUserId, CreatedAtUtc = now, ExpiresAtUtc = now.AddDays(7)
                };
                context.Add(invite);
                Record(requestId, "ledger-invitation.create", hash, invite.Id, actor);
                await context.SaveChangesAsync(token);
            }
            return new LedgerCreatedBody(new { invitation = Invitation(invite), invitations = (await InvitationsAsync(id, token)).Invitations }, previous is not null);
        }, token);

    public Task<object> RevokeInvitationAsync(string id, string invitationId, CurrentSession actor, CancellationToken token) =>
        InLedgerAsync(id, actor, member => member.Role == "Admin", async _ =>
        {
            var invite = await context.Set<LedgerInvitation>().SingleOrDefaultAsync(row => row.LedgerId == id && row.Id == invitationId, token) ?? throw Missing();
            // 参加確定後は参加を取り消さず、取消済みへの再送も状態を戻さない。
            if (invite.UsedAtUtc is null && invite.RevokedAtUtc is null)
            {
                invite.RevokedAtUtc = Now;
                await context.SaveChangesAsync(token);
            }
            return await InvitationsAsync(id, token);
        }, token);

    public async Task<object> PreviewInvitationAsync(string value, CurrentSession actor, CancellationToken token)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        await RequireLoginAsync(actor, token);
        var invite = await FindInvitationAsync(value, token);
        InvitationActive(invite);
        var member = await context.Set<LedgerMember>().AnyAsync(row => row.LedgerId == invite.LedgerId && row.AuthUserId == actor.Session.AuthUserId, token);
        var ledger = await context.Set<Ledger>().AsNoTracking().SingleAsync(row => row.Id == invite.LedgerId, token);
        await RequireLoginAsync(actor, token);
        await transaction.CommitAsync(token);
        return new InvitationPreview(member ? "already_member" : "valid", ledger.Id, ledger.Name, DateTime.SpecifyKind(invite.ExpiresAtUtc, DateTimeKind.Utc));
    }

    public async Task<object> AcceptInvitationAsync(string value, CurrentSession actor, CancellationToken token)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        await RequireLoginAsync(actor, token);
        var invite = await FindInvitationAsync(value, token);
        InvitationActive(invite);
        var accountId = actor.Session.AuthUserId;
        if (await context.Set<LedgerMember>().AnyAsync(row => row.LedgerId == invite.LedgerId && row.AuthUserId == accountId, token))
        {
            var ledgerName = await context.Set<Ledger>().Where(row => row.Id == invite.LedgerId).Select(row => row.Name).SingleAsync(token);
            await RequireLoginAsync(actor, token);
            await transaction.CommitAsync(token);
            return new InvitationPreview("already_member", invite.LedgerId, ledgerName, DateTime.SpecifyKind(invite.ExpiresAtUtc, DateTimeKind.Utc));
        }
        context.Add(new LedgerMember
        {
            LedgerId = invite.LedgerId, AuthUserId = accountId, Role = "Viewer", JoinedAtUtc = Now, Revision = Version()
        });
        invite.UsedByAuthUserId = accountId;
        invite.UsedAtUtc = Now;
        await context.SaveChangesAsync(token);
        await ledgers.RememberOpenedAsync(invite.LedgerId, actor, token);
        await RequireLoginAsync(actor, token);
        if (!await context.Set<LedgerMember>().AsNoTracking().AnyAsync(row => row.LedgerId == invite.LedgerId &&
            row.AuthUserId == accountId && row.Role == "Viewer" && row.PersonId == null, token))
            throw new InvalidOperationException("新しい参加者の状態を確認できません。");
        var saved = await context.Set<LedgerInvitation>().AsNoTracking().SingleAsync(row => row.Id == invite.Id, token);
        if (saved.ExpiresAtUtc <= Now) throw new LedgerApiFault(409, "invitation_expired", "招待の期限が切れています。");
        if (saved.UsedByAuthUserId != accountId || saved.UsedAtUtc is null || saved.RevokedAtUtc is not null)
            throw new InvalidOperationException("招待の使用状態を確認できません。");
        var ledger = await ledgers.VisibleLedgers(actor, invite.LedgerId).SingleAsync(token);
        await transaction.CommitAsync(token);
        return new { status = "joined", ledger };
    }

    private async Task<LedgerInvitation> FindInvitationAsync(string value, CancellationToken token)
    {
        var invite = await context.Set<LedgerInvitation>().SingleOrDefaultAsync(row => row.Token == value, token);
        return invite ?? throw new LedgerApiFault(404, "invitation_invalid", "無効な招待です。");
    }
    private void InvitationActive(LedgerInvitation invite)
    {
        if (invite.UsedAtUtc is not null) throw new LedgerApiFault(409, "invitation_used", "この招待リンクは使用済みです。");
        if (invite.RevokedAtUtc is not null) throw new LedgerApiFault(409, "invitation_revoked", "無効な招待です。");
        if (invite.ExpiresAtUtc <= Now) throw new LedgerApiFault(409, "invitation_expired", "招待の期限が切れています。");
    }
}
