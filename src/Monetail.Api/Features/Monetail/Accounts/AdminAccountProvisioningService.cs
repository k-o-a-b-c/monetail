using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Monetail.Api.Authentication;

namespace Monetail.Api.Features.Monetail.Accounts;

/// <summary>初回だけ生コードを返し、再送時には作成済みアカウントだけを返す。</summary>
public sealed record AccountCreationResponse(string AuthUserId, int UserCode, string? Code, DateTime? ExpiresAtUtc)
{
    /// <summary>応答をログへ出しても登録コードを含めない。</summary>
    public override string ToString() => $"Account created for {AuthUserId}";
}

/// <summary>初回発行・再送・入力や認証の変化をHTTP受付へ伝える。</summary>
public enum AccountCreationStatus { Created, Replayed, PersonNotFound, AlreadyExists, RequestConflict, AuthenticationChanged }

/// <summary>発行の結果と、成功時だけ公開できる応答を保持する。</summary>
public sealed record AccountCreationResult(AccountCreationStatus Status, AccountCreationResponse? Response = null);

/// <summary>再発行・対象不備・管理者の認証変更をHTTP受付へ伝える。</summary>
public enum EnrollmentCodeReissueStatus { Issued, NotFound, TargetIsAdmin, NotEligible, AuthenticationChanged }

/// <summary>再発行の結果と、成功した場合だけ新しいコードを保持する。</summary>
public sealed record EnrollmentCodeReissueResult(EnrollmentCodeReissueStatus Status, IssuedEnrollmentCode? IssuedCode = null);

/// <summary>一般アカウントの初回発行と登録コードの再発行を、現在の管理者確認付きで行う。</summary>
public sealed class AdminAccountProvisioningService(AuthDbContext context, UserManager<AuthUser> users,
    AdminProvisioningService codes, IAccountPersonLookup people, TimeProvider time,
    ILogger<AdminAccountProvisioningService> logger)
{
    /// <summary>現在の管理者を再確認し、人物対応・初回コード・要求記録を一つのトランザクションで確定する。</summary>
    public async Task<AccountCreationResult> CreateAsync(Guid requestId, int userCode, HttpContext http, CancellationToken token)
    {
        await EnsureMigratedAsync(token);
        var current = (CurrentSession)http.Items[typeof(CurrentSession)]!;
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        if (!await IsCurrentAdminAsync(current, token)) return new(AccountCreationStatus.AuthenticationChanged);
        var key = requestId.ToString("N");
        var previous = await context.Set<AccountCreationRequest>().AsNoTracking().SingleOrDefaultAsync(row => row.RequestId == key, token);
        if (previous is not null)
            return previous.CreatedByAuthUserId == current.Session.AuthUserId && previous.UserCode == userCode
                ? new(AccountCreationStatus.Replayed, new(previous.AuthUserId, previous.UserCode, null, null))
                : new(AccountCreationStatus.RequestConflict);
        if (!await context.Set<LegacyPerson>().AnyAsync(person => person.Code == userCode, token))
            return new(AccountCreationStatus.PersonNotFound);
        if (await context.UserLinks.AnyAsync(link => link.UserCode == userCode, token))
            return new(AccountCreationStatus.AlreadyExists);
        var user = new AuthUser { Id = Guid.NewGuid().ToString("N") };
        user.UserName = user.Id;
        if (!(await users.CreateAsync(user)).Succeeded)
            throw new InvalidOperationException("認証アカウントを保存できません。");
        context.UserLinks.Add(new AuthUserLink { AuthUserId = user.Id, UserCode = userCode });
        var issued = await codes.AddCodeAsync(user.Id, token);
        context.Set<AccountCreationRequest>().Add(new AccountCreationRequest
        {
            RequestId = key, CreatedByAuthUserId = current.Session.AuthUserId, AuthUserId = user.Id,
            UserCode = userCode, CreatedAtUtc = time.GetUtcNow().UtcDateTime
        });
        await context.SaveChangesAsync(token);
        if (!await IsCurrentAdminAsync(current, token)) return new(AccountCreationStatus.AuthenticationChanged);
        await transaction.CommitAsync(token);
        logger.LogInformation("一般アカウントを発行しました。実行者={ActorId} 認証ID={AuthUserId} 対象人物={PersonCode} 日時={OccurredAtUtc} 結果=成功",
            current.Session.AuthUserId, user.Id, userCode, time.GetUtcNow());
        return new(AccountCreationStatus.Created, new(user.Id, userCode, issued.Code, issued.ExpiresAtUtc));
    }

    /// <summary>停止中・鍵なしの一般アカウントへ、古い未使用コードの失効と新コード保存を同時に行う。</summary>
    public async Task<EnrollmentCodeReissueResult> ReissueCodeAsync(string authUserId, HttpContext http, CancellationToken token)
    {
        await EnsureMigratedAsync(token);
        var current = (CurrentSession)http.Items[typeof(CurrentSession)]!;
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        if (!await IsCurrentAdminAsync(current, token)) return new(EnrollmentCodeReissueStatus.AuthenticationChanged);
        var target = await FindReissueTargetAsync(authUserId, token);
        if (target.Status != EnrollmentCodeReissueStatus.Issued) return new(target.Status);
        var issued = await codes.AddCodeAsync(authUserId, token);
        await context.SaveChangesAsync(token);
        if (!await IsCurrentAdminAsync(current, token)) return new(EnrollmentCodeReissueStatus.AuthenticationChanged);
        var latest = await FindReissueTargetAsync(authUserId, token);
        if (latest.Status != EnrollmentCodeReissueStatus.Issued) return new(latest.Status);
        if (latest.Person!.UserCode != target.Person!.UserCode) return new(EnrollmentCodeReissueStatus.NotEligible);
        await transaction.CommitAsync(token);
        logger.LogInformation("一般アカウントの登録コードを再発行しました。実行者={ActorId} 認証ID={AuthUserId} 対象人物={PersonCode} 日時={OccurredAtUtc} 結果=成功",
            current.Session.AuthUserId, authUserId, target.Person.UserCode, time.GetUtcNow());
        return new(EnrollmentCodeReissueStatus.Issued, issued);
    }

    /// <summary>現在の一般アカウント・人物対応・停止状態・パスキーなしを確認する。</summary>
    private async Task<(EnrollmentCodeReissueStatus Status, AccountPerson? Person)> FindReissueTargetAsync(string id, CancellationToken token)
    {
        var user = await context.Users.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token);
        if (user is null) return (EnrollmentCodeReissueStatus.NotFound, null);
        if (await users.IsInRoleAsync(user, "Admin")) return (EnrollmentCodeReissueStatus.TargetIsAdmin, null);
        var person = await people.FindAsync(id, token);
        if (person is null) return (EnrollmentCodeReissueStatus.NotFound, null);
        if (user.IsEnabled || (await users.GetPasskeysAsync(user)).Count > 0) return (EnrollmentCodeReissueStatus.NotEligible, null);
        return (EnrollmentCodeReissueStatus.Issued, person);
    }

    /// <summary>HTTP操作ではDB移行を適用せず、未適用なら発行を拒否する。</summary>
    private async Task EnsureMigratedAsync(CancellationToken token)
    {
        if ((await context.Database.GetPendingMigrationsAsync(token)).Any())
            throw new InvalidOperationException("先にmigrate-auth-dbを実行してください。");
    }

    /// <summary>DBの現在のセッション・利用可否・人物対応・Adminロール・直近5分を再確認する。</summary>
    private async Task<bool> IsCurrentAdminAsync(CurrentSession current, CancellationToken token)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var allowed = await (from session in context.Sessions
                             join user in context.Users on session.AuthUserId equals user.Id
                             join membership in context.UserRoles on user.Id equals membership.UserId
                             join role in context.Roles on membership.RoleId equals role.Id
                             where session.IdHash == current.Session.IdHash && user.Id == current.Session.AuthUserId &&
                                 user.IsEnabled && role.Name == "Admin" && session.RevokedAtUtc == null &&
                                 session.ExpiresAtUtc > now && session.VerifiedAtUtc <= now && session.VerifiedAtUtc > now.AddMinutes(-5)
                             select user.Id).AnyAsync(token);
        if (!allowed) return false;
        var person = await people.FindAsync(current.Session.AuthUserId, token);
        return person is not null && person.UserCode == current.UserCode;
    }
}
