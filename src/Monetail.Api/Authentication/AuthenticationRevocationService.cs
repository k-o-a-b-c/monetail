using Microsoft.EntityFrameworkCore;
using Monetail.Api.Persistence;

namespace Monetail.Api.Authentication;

/// <summary>アカウントを停止し、パスキー・セッション・未使用コードをまとめて失効させる。</summary>
public sealed class AuthenticationRevocationService(ApiDbContext context, TimeProvider time,
    ILogger<AuthenticationRevocationService> logger)
{
    /// <summary>対象アカウントの認証情報を全失効し、通常操作では最後の有効な管理者を保護する。</summary>
    public async Task RevokeAccountAsync(string authUserId, bool emergencyRecovery = false, CancellationToken token = default)
    {
        await DatabaseInitializer.EnsureReadyAsync(context, token);
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        var target = await context.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == authUserId, token)
            ?? throw new InvalidOperationException("指定したアカウントを確認できません。");
        var administrator = await (from assignment in context.UserRoles
                                   join role in context.Roles on assignment.RoleId equals role.Id
                                   where assignment.UserId == target.Id && role.Name == "Admin"
                                   select assignment).AnyAsync(token);
        if (!emergencyRecovery && target.IsEnabled && administrator && !await HasOtherAdministratorAsync(target.Id, token))
            throw new InvalidOperationException("最後の有効な管理者は通常の全失効では停止できません。紛失復旧時は--emergency-recoveryを明示してください。");
        var now = time.GetUtcNow().UtcDateTime;
        await RevokeAsync(target.Id, now, token);
        await transaction.CommitAsync(token);
        logger.LogInformation("認証情報を全失効しました。認証ID={AuthUserId} 緊急復旧={EmergencyRecovery} 日時={OccurredAtUtc} 結果=成功",
            target.Id, emergencyRecovery, now);
    }

    /// <summary>バックアップ復元後に、全アカウントの認証情報を同じトランザクションで失効させる。</summary>
    public async Task ResetAfterRestoreAsync(CancellationToken token = default)
    {
        await DatabaseInitializer.EnsureReadyAsync(context, token);
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        var now = time.GetUtcNow().UtcDateTime;
        await RevokeAsync(null, now, token);
        await transaction.CommitAsync(token);
        logger.LogInformation("復元後の認証情報を全失効しました。対象=全アカウント 日時={OccurredAtUtc} 結果=成功", now);
    }

    /// <summary>パスキーを持つ別の有効な管理者アカウントを確認する。</summary>
    private Task<bool> HasOtherAdministratorAsync(string excludedUserId, CancellationToken token) =>
        (from user in context.Users
         join assignment in context.UserRoles on user.Id equals assignment.UserId
         join role in context.Roles on assignment.RoleId equals role.Id
         where user.Id != excludedUserId && user.IsEnabled && role.Name == "Admin" &&
             context.UserPasskeys.Any(key => key.UserId == user.Id)
         select user).AnyAsync(token);

    /// <summary>指定アカウントまたは全体を停止し、鍵削除とセッション・未使用コードの取消を行う。</summary>
    private async Task RevokeAsync(string? authUserId, DateTime now, CancellationToken token)
    {
        await context.Users.Where(user => (authUserId == null || user.Id == authUserId) && user.IsEnabled)
            .ExecuteUpdateAsync(update => update.SetProperty(user => user.IsEnabled, false), token);
        await context.UserPasskeys.Where(key => authUserId == null || key.UserId == authUserId).ExecuteDeleteAsync(token);
        await context.Sessions.Where(session => (authUserId == null || session.AuthUserId == authUserId) && session.RevokedAtUtc == null)
            .ExecuteUpdateAsync(update => update.SetProperty(session => session.RevokedAtUtc, now), token);
        await context.EnrollmentCodes.Where(code => (authUserId == null || code.AuthUserId == authUserId) &&
            code.ConsumedAtUtc == null && code.RevokedAtUtc == null)
            .ExecuteUpdateAsync(update => update.SetProperty(code => code.RevokedAtUtc, now), token);
    }

    /// <summary>明示的なDB移行が済んでいない場合は認証情報の変更を拒否する。</summary>
    private async Task EnsureMigratedAsync(CancellationToken token)
    {
        await DatabaseInitializer.EnsureReadyAsync(context, token);
    }
}
