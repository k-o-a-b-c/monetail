using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Monetail.Api.Persistence;

namespace Monetail.Api.Authentication;

/// <summary>初期の全体管理者作成と、登録コードの明示的な再発行を行う。</summary>
public sealed class AdminProvisioningService(ApiDbContext context, UserManager<AuthUser> users,
    RoleManager<IdentityRole> roles, TimeProvider time, ILogger<AdminProvisioningService> logger)
{
    /// <summary>最初の管理者とコードを同時に作成し、再実行では既存アカウントを変更しない。</summary>
    public async Task<IssuedEnrollmentCode?> InitializeAdminAsync(CancellationToken token = default)
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
        var issued = await AddCodeAsync(user.Id, token);
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        logger.LogInformation("初期管理者を作成しました。認証ID={AuthUserId} 日時={OccurredAtUtc} 結果=成功",
            user.Id, time.GetUtcNow());
        return issued;
    }

    /// <summary>利用停止中・登録待ちの同じアカウントへコードを再発行する。</summary>
    public async Task<IssuedEnrollmentCode> IssueEnrollmentCodeAsync(string authUserId, CancellationToken token = default)
    {
        await DatabaseInitializer.EnsureReadyAsync(context, token);
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        var user = await users.FindByIdAsync(authUserId)
            ?? throw new InvalidOperationException("指定したアカウントを確認できません。");
        if (user.IsEnabled || (await users.GetPasskeysAsync(user)).Count > 0)
            throw new InvalidOperationException("利用中またはパスキーが残るアカウントです。先に全失効が必要です。");
        var issued = await AddCodeAsync(user.Id, token);
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        logger.LogInformation("登録コードを発行しました。認証ID={AuthUserId} 日時={OccurredAtUtc} 結果=成功",
            user.Id, time.GetUtcNow());
        return issued;
    }

    /// <summary>呼出元のトランザクション内で古い未使用コードを失効させ、新コードのハッシュだけを保存対象へ追加する。</summary>
    internal async Task<IssuedEnrollmentCode> AddCodeAsync(string authUserId, CancellationToken token)
    {
        var now = time.GetUtcNow().UtcDateTime;
        await context.EnrollmentCodes.Where(code => code.AuthUserId == authUserId &&
                code.ConsumedAtUtc == null && code.RevokedAtUtc == null)
            .ExecuteUpdateAsync(update => update.SetProperty(code => code.RevokedAtUtc, now), token);
        var rawCode = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var expires = now.AddMinutes(10);
        context.EnrollmentCodes.Add(new EnrollmentCode
        {
            CodeHash = SHA256.HashData(Encoding.UTF8.GetBytes(rawCode)), AuthUserId = authUserId,
            CreatedAtUtc = now, ExpiresAtUtc = expires
        });
        return new IssuedEnrollmentCode(authUserId, rawCode, expires);
    }

    /// <summary>Identityの保存失敗をトランザクション全体の失敗にする。</summary>
    private static void RequireSuccess(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException("認証アカウントの保存に失敗しました。");
    }
}
