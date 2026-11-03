using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Monetail.Api.Persistence;

namespace Monetail.Api.Authentication;

/// <summary>初期の全体管理者作成と、登録リンクの明示的な再発行を行う。</summary>
public sealed class AdminProvisioningService(ApiDbContext context, UserManager<AuthUser> users,
    RoleManager<IdentityRole> roles, TimeProvider time, IConfiguration configuration, ILogger<AdminProvisioningService> logger)
{
    /// <summary>最初の管理者とリンクを同時に作成し、再実行では既存アカウントを変更しない。</summary>
    public async Task<IssuedEnrollmentInvitation?> InitializeAdminAsync(CancellationToken token = default)
    {
        await DatabaseInitializer.EnsureReadyAsync(context, token);
        // SQLiteの既定の書込トランザクションで、複数接続からの初期作成も直列化する。
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        var administrators = await users.GetUsersInRoleAsync("Admin");
        if (administrators.Count > 0)
        {
            logger.LogInformation("初期管理者は作成済みです。日時={OccurredAtUtc} 結果=変更なし", time.GetUtcNow());
            return null;
        }

        var user = new AuthUser { Id = Guid.NewGuid().ToString("N") };
        user.UserName = user.Id;
        RequireSuccess(await users.CreateAsync(user));
        if (!await roles.RoleExistsAsync("Admin"))
            RequireSuccess(await roles.CreateAsync(new IdentityRole("Admin")));
        RequireSuccess(await users.AddToRoleAsync(user, "Admin"));
        var issued = await AddInvitationAsync(user.Id, TimeSpan.FromMinutes(30), EnrollmentCode.InitialAdminPurpose, token);
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        logger.LogInformation("初期管理者を作成しました。認証ID={AuthUserId} 日時={OccurredAtUtc} 結果=成功",
            user.Id, time.GetUtcNow());
        return issued;
    }

    /// <summary>利用停止中・登録待ちの同じアカウントへリンクを再発行する。</summary>
    public async Task<IssuedEnrollmentInvitation> IssueEnrollmentInvitationAsync(string authUserId, CancellationToken token = default)
        => await IssueAsync(authUserId, recovery: false, token);

    /// <summary>運営者が明示した緊急復旧だけに、従来の10分を維持してリンクを発行する。</summary>
    public async Task<IssuedEnrollmentInvitation> IssueRecoveryLinkAsync(string authUserId, CancellationToken token = default)
        => await IssueAsync(authUserId, recovery: true, token);

    private async Task<IssuedEnrollmentInvitation> IssueAsync(string authUserId, bool recovery, CancellationToken token)
    {
        await DatabaseInitializer.EnsureReadyAsync(context, token);
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        var user = await users.FindByIdAsync(authUserId)
            ?? throw new InvalidOperationException("指定したアカウントを確認できません。");
        if (user.IsEnabled || (await users.GetPasskeysAsync(user)).Count > 0)
            throw new InvalidOperationException("利用中またはパスキーが残るアカウントです。先に全失効が必要です。");
        if (!recovery && await WasRegisteredAsync(user, token))
            throw new InvalidOperationException("登録済みアカウントの復旧にはissue-recovery-linkを明示してください。");
        var initialAdmin = !recovery && await users.IsInRoleAsync(user, "Admin");
        var issued = await AddInvitationAsync(user.Id, recovery ? TimeSpan.FromMinutes(10)
            : initialAdmin ? TimeSpan.FromMinutes(30) : TimeSpan.FromDays(7),
            recovery ? EnrollmentCode.RecoveryPurpose : initialAdmin ? EnrollmentCode.InitialAdminPurpose : EnrollmentCode.InvitationPurpose, token);
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        logger.LogInformation("登録リンクを発行しました。認証ID={AuthUserId} 復旧={Recovery} 日時={OccurredAtUtc} 結果=成功",
            user.Id, recovery, time.GetUtcNow());
        return issued;
    }

    /// <summary>呼出元のトランザクション内で古い未使用リンクを失効させ、新リンクのハッシュだけを保存対象へ追加する。</summary>
    internal async Task<IssuedEnrollmentInvitation> AddInvitationAsync(string authUserId, TimeSpan validity, string purpose, CancellationToken token)
    {
        var origin = PasskeySettings.ReadOrigin(configuration).GetLeftPart(UriPartial.Authority);
        var now = time.GetUtcNow().UtcDateTime;
        await context.EnrollmentCodes.Where(code => code.AuthUserId == authUserId &&
                code.ConsumedAtUtc == null && code.RevokedAtUtc == null)
            .ExecuteUpdateAsync(update => update.SetProperty(code => code.RevokedAtUtc, now), token);
        var rawCode = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var expires = now.Add(validity);
        context.EnrollmentCodes.Add(new EnrollmentCode
        {
            CodeHash = SHA256.HashData(Encoding.UTF8.GetBytes(rawCode)), AuthUserId = authUserId,
            CreatedAtUtc = now, ExpiresAtUtc = expires, Purpose = purpose
        });
        return new IssuedEnrollmentInvitation(authUserId, origin + "/auth/enroll#token=" + rawCode, expires);
    }

    /// <summary>表示名・消費済み登録・セッションの履歴から、登録待ちと登録済み停止を分ける。</summary>
    internal async Task<bool> WasRegisteredAsync(AuthUser user, CancellationToken token) => user.DisplayName is not null ||
        await context.EnrollmentCodes.AnyAsync(row => row.AuthUserId == user.Id && row.ConsumedAtUtc != null, token) ||
        await context.Sessions.AnyAsync(row => row.AuthUserId == user.Id, token);

    /// <summary>Identityの保存失敗をトランザクション全体の失敗にする。</summary>
    private static void RequireSuccess(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException("認証アカウントの保存に失敗しました。");
    }
}
