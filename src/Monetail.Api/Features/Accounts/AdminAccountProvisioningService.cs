using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Monetail.Api.Authentication;
using Monetail.Api.Persistence;

namespace Monetail.Api.Features.Accounts;

/// <summary>初回だけ生コードを返し、再送時には作成済みアカウントだけを返す。</summary>
public sealed record AccountCreationResponse(string AuthUserId, string? EnrollmentUrl, DateTime? ExpiresAtUtc)
{
    /// <summary>応答をログへ出しても登録コードを含めない。</summary>
    public override string ToString() => $"Account created for {AuthUserId}";
}

/// <summary>初回発行・再送・入力や認証の変化をHTTP受付へ伝える。</summary>
public enum AccountCreationStatus { Created, Replayed, RequestConflict, AuthenticationChanged }

/// <summary>発行の結果と、成功時だけ公開できる応答を保持する。</summary>
public sealed record AccountCreationResult(AccountCreationStatus Status, AccountCreationResponse? Response = null);

/// <summary>再発行・対象不備・管理者の認証変更をHTTP受付へ伝える。</summary>
public enum EnrollmentInvitationReissueStatus { Issued, NotFound, NotEligible, AuthenticationChanged }

/// <summary>再発行の結果と、成功した場合だけ新しいコードを保持する。</summary>
public sealed record EnrollmentInvitationReissueResult(EnrollmentInvitationReissueStatus Status, IssuedEnrollmentInvitation? Invitation = null);

/// <summary>現在の全体管理者を確認し、一般アカウントの初回発行と既存アカウントのコード再発行を行う。</summary>
public sealed class AdminAccountProvisioningService(ApiDbContext context, UserManager<AuthUser> users,
    AdminProvisioningService codes, TimeProvider time,
    ILogger<AdminAccountProvisioningService> logger)
{
    /// <summary>現在の全体管理者を再確認し、新規アカウント・初回コード・要求記録を同時に保存する。</summary>
    public async Task<AccountCreationResult> CreateAsync(Guid requestId, HttpContext http, CancellationToken token)
    {
        await DatabaseInitializer.EnsureReadyAsync(context, token);
        var current = (CurrentSession)http.Items[typeof(CurrentSession)]!;
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        if (!await IsCurrentAdminAsync(current, token)) return new(AccountCreationStatus.AuthenticationChanged);
        var key = requestId.ToString("N");
        var previous = await context.Set<AccountCreationRequest>().AsNoTracking().SingleOrDefaultAsync(row => row.RequestId == key, token);
        if (previous is not null)
            return previous.CreatedByAuthUserId == current.Session.AuthUserId
                ? new(AccountCreationStatus.Replayed, new(previous.AuthUserId, null, null))
                : new(AccountCreationStatus.RequestConflict);
        var user = new AuthUser { Id = Guid.NewGuid().ToString("N") };
        user.UserName = user.Id;
        if (!(await users.CreateAsync(user)).Succeeded)
            throw new InvalidOperationException("認証アカウントを保存できません。");
        var issued = await codes.AddInvitationAsync(user.Id, TimeSpan.FromDays(7), EnrollmentCode.InvitationPurpose, token);
        context.Set<AccountCreationRequest>().Add(new AccountCreationRequest
        {
            RequestId = key, CreatedByAuthUserId = current.Session.AuthUserId, AuthUserId = user.Id,
            CreatedAtUtc = time.GetUtcNow().UtcDateTime
        });
        await context.SaveChangesAsync(token);
        if (!await IsCurrentAdminAsync(current, token)) return new(AccountCreationStatus.AuthenticationChanged);
        await transaction.CommitAsync(token);
        logger.LogInformation("一般アカウントを発行しました。実行者={ActorId} 認証ID={AuthUserId} 日時={OccurredAtUtc} 結果=成功",
            current.Session.AuthUserId, user.Id, time.GetUtcNow());
        return new(AccountCreationStatus.Created, new(user.Id, issued.EnrollmentUrl, issued.ExpiresAtUtc));
    }

    /// <summary>停止中・鍵なしのアカウントへ、古い未使用コードの失効と新コード保存を同時に行う。</summary>
    public async Task<EnrollmentInvitationReissueResult> ReissueInvitationAsync(string authUserId, HttpContext http, CancellationToken token)
    {
        await DatabaseInitializer.EnsureReadyAsync(context, token);
        var current = (CurrentSession)http.Items[typeof(CurrentSession)]!;
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        if (!await IsCurrentAdminAsync(current, token)) return new(EnrollmentInvitationReissueStatus.AuthenticationChanged);
        var target = await FindReissueTargetAsync(authUserId, token);
        if (target != EnrollmentInvitationReissueStatus.Issued) return new(target);
        var user = (await users.FindByIdAsync(authUserId))!;
        var initialAdmin = await users.IsInRoleAsync(user, "Admin");
        var issued = await codes.AddInvitationAsync(authUserId, initialAdmin ? TimeSpan.FromMinutes(30) : TimeSpan.FromDays(7),
            initialAdmin ? EnrollmentCode.InitialAdminPurpose : EnrollmentCode.InvitationPurpose, token);
        await context.SaveChangesAsync(token);
        if (!await IsCurrentAdminAsync(current, token)) return new(EnrollmentInvitationReissueStatus.AuthenticationChanged);
        var latest = await FindReissueTargetAsync(authUserId, token);
        if (latest != EnrollmentInvitationReissueStatus.Issued) return new(latest);
        await transaction.CommitAsync(token);
        logger.LogInformation("登録コードを再発行しました。実行者={ActorId} 認証ID={AuthUserId} 日時={OccurredAtUtc} 結果=成功",
            current.Session.AuthUserId, authUserId, time.GetUtcNow());
        return new(EnrollmentInvitationReissueStatus.Issued, issued);
    }

    /// <summary>対象アカウントの存在・停止状態・パスキーなしを確認する。</summary>
    private async Task<EnrollmentInvitationReissueStatus> FindReissueTargetAsync(string id, CancellationToken token)
    {
        var user = await context.Users.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token);
        if (user is null) return EnrollmentInvitationReissueStatus.NotFound;
        if (user.IsEnabled || (await users.GetPasskeysAsync(user)).Count > 0 || await codes.WasRegisteredAsync(user, token)) return EnrollmentInvitationReissueStatus.NotEligible;
        return EnrollmentInvitationReissueStatus.Issued;
    }

    /// <summary>現在の管理者を保存前後で確認し、有効な未使用リンクだけを取消する。</summary>
    public async Task<EnrollmentInvitationReissueStatus> CancelInvitationAsync(string id, HttpContext http, CancellationToken token)
    {
        await DatabaseInitializer.EnsureReadyAsync(context, token);
        var current = (CurrentSession)http.Items[typeof(CurrentSession)]!;
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        if (!await IsCurrentAdminAsync(current, token)) return EnrollmentInvitationReissueStatus.AuthenticationChanged;
        if (!await context.Users.AnyAsync(user => user.Id == id, token)) return EnrollmentInvitationReissueStatus.NotFound;
        var now = time.GetUtcNow().UtcDateTime;
        await context.EnrollmentCodes.Where(row => row.AuthUserId == id && row.ConsumedAtUtc == null &&
            row.RevokedAtUtc == null && row.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.RevokedAtUtc, now), token);
        if (!await IsCurrentAdminAsync(current, token)) return EnrollmentInvitationReissueStatus.AuthenticationChanged;
        await transaction.CommitAsync(token);
        logger.LogInformation("登録リンクを取消しました。実行者={ActorId} 認証ID={AuthUserId}", current.Session.AuthUserId, id);
        return EnrollmentInvitationReissueStatus.Issued;
    }

    /// <summary>DBの現在のセッション・アカウント利用可否・Adminロール・直近5分を再確認する。</summary>
    private Task<bool> IsCurrentAdminAsync(CurrentSession current, CancellationToken token)
    {
        var now = time.GetUtcNow().UtcDateTime;
        return (from session in context.Sessions
                             join user in context.Users on session.AuthUserId equals user.Id
                             join membership in context.UserRoles on user.Id equals membership.UserId
                             join role in context.Roles on membership.RoleId equals role.Id
                             where session.IdHash == current.Session.IdHash && user.Id == current.Session.AuthUserId &&
                                 user.IsEnabled && role.Name == "Admin" && session.RevokedAtUtc == null &&
                                 session.ExpiresAtUtc > now && session.VerifiedAtUtc <= now && session.VerifiedAtUtc > now.AddMinutes(-5)
                             select user.Id).AnyAsync(token);
    }
}
