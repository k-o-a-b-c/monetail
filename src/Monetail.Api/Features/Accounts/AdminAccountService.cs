using Microsoft.EntityFrameworkCore;
using Monetail.Api.Authentication;
using Monetail.Api.Persistence;

namespace Monetail.Api.Features.Accounts;

/// <summary>秘密の鍵・セッション・復旧リンクを含めず管理用の状態を返す。</summary>
public sealed record AdminAccountSummary(string AuthUserId, string? DisplayName, bool IsEnabled,
    string? AdminNote, bool IsGlobalAdmin, string RegistrationStatus, DateTime? InvitationExpiresAtUtc);

/// <summary>認証ストアの登録履歴・招待用途・現在のロールから管理一覧を作る。</summary>
public sealed class AdminAccountService(ApiDbContext context, AdminProvisioningService provisioning, TimeProvider time)
{
    public async Task<AdminAccountSummary[]> ListAsync(CancellationToken token)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var rows = await context.Users.AsNoTracking().OrderBy(user => user.Id).Select(user => new
        {
            user.Id, user.DisplayName, user.IsEnabled,
            AdminNote = context.Set<AccountAdminNote>().Where(note => note.AuthUserId == user.Id).Select(note => note.AdminNote).FirstOrDefault(),
            Registered = provisioning.RegisteredAccounts.Any(registered => registered.Id == user.Id),
            Admin = context.UserRoles.Any(membership => membership.UserId == user.Id &&
                context.Roles.Any(role => role.Id == membership.RoleId && role.Name == "Admin")),
            Expiry = context.EnrollmentCodes.Where(link => link.AuthUserId == user.Id &&
                link.ConsumedAtUtc == null && link.RevokedAtUtc == null && link.ExpiresAtUtc > now &&
                (link.Purpose == EnrollmentCode.InvitationPurpose || link.Purpose == EnrollmentCode.InitialAdminPurpose))
                .Select(link => (DateTime?)link.ExpiresAtUtc).FirstOrDefault()
        }).ToArrayAsync(token);
        return rows.Select(row => new AdminAccountSummary(row.Id, row.DisplayName, row.IsEnabled, row.AdminNote, row.Admin,
            row.IsEnabled ? "active" : row.Registered ? "suspended" : row.Expiry is not null ? "invited" : "unregistered",
            !row.IsEnabled && !row.Registered && row.Expiry is { } expiry ? DateTime.SpecifyKind(expiry, DateTimeKind.Utc) : null)).ToArray();
    }
}
