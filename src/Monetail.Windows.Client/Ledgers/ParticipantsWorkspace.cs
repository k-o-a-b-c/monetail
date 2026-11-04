using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Client.Ledgers;

/// <summary>応答喪失後も同じ要求IDを保持する。トークンや招待URLは保存しない。</summary>
public sealed record LedgerInvitationDraft
{
    public Guid RequestId { get; } = Guid.NewGuid();
}

/// <summary>参加者と家計簿招待でも既存の認証更新・別アカウント検知を共用する。</summary>
public sealed class ParticipantsWorkspace(MonetailApiClient api, ClientSession session, Guid ledgerId)
{
    public Task<MembersInfo> ListAsync(CancellationToken token = default) =>
        session.ExecuteAuthenticatedAsync((access, ct) => api.ListMembersAsync(access, ledgerId, ct), token);
    public Task<MemberEditInfo> EditAsync(Guid accountId, CancellationToken token = default) =>
        session.ExecuteAuthenticatedAsync((access, ct) => api.GetMemberEditAsync(access, ledgerId, accountId, ct), token);
    public Task<MembersInfo> SaveAsync(MemberInfo member, string role, Guid? personId, Guid currentAccountId, CancellationToken token = default) =>
        session.ExecuteAuthenticatedAsync((access, ct) => api.UpdateMemberAsync(access, ledgerId, member, role, personId, member.AccountId == currentAccountId, ct), token);
    public Task<MembersInfo> ExcludeAsync(MemberInfo member, Guid currentAccountId, CancellationToken token = default)
    {
        if (member.AccountId == currentAccountId) throw new InvalidOperationException("自分は除外できません。");
        return session.ExecuteAuthenticatedAsync((access, ct) => api.ExcludeMemberAsync(access, ledgerId, member, ct), token);
    }
    public Task<LedgerInvitationsInfo> InvitationsAsync(CancellationToken token = default) =>
        session.ExecuteAuthenticatedAsync((access, ct) => api.ListInvitationsAsync(access, ledgerId, ct), token);
    public Task<LedgerInvitationsInfo> CreateInvitationAsync(LedgerInvitationDraft draft, CancellationToken token = default) =>
        session.ExecuteAuthenticatedAsync((access, ct) => api.CreateInvitationAsync(access, ledgerId, draft.RequestId, ct), token);
    public Task<LedgerInvitationsInfo> CancelInvitationAsync(Guid id, CancellationToken token = default) =>
        session.ExecuteAuthenticatedAsync((access, ct) => api.CancelLedgerInvitationAsync(access, ledgerId, id, ct), token);
}
