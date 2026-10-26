using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Monetail.Api.Authentication;

/// <summary>既存人物への初期管理者作成と、登録コードの明示的な再発行を行う。</summary>
public sealed class AdminProvisioningService(AuthDbContext context, UserManager<AuthUser> users,
    RoleManager<IdentityRole> roles, TimeProvider time, ILogger<AdminProvisioningService> logger)
{
    /// <summary>最初の管理者・人物対応・コードを同時に作成し、再実行では変更しない。</summary>
    public async Task<IssuedEnrollmentCode?> InitializeAdminAsync(int personCode, CancellationToken token = default)
    {
        await EnsureMigratedAsync(token);
        // SQLiteの既定の書込トランザクションで、複数接続からの初期作成も直列化する。
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        if (!await context.Set<LegacyPerson>().AnyAsync(person => person.Code == personCode, token))
            throw new InvalidOperationException("指定した人物は登録されていません。");

        var administrators = await users.GetUsersInRoleAsync("Admin");
        var link = await context.UserLinks.SingleOrDefaultAsync(row => row.UserCode == personCode, token);
        if (administrators.Count > 0)
        {
            if (link is not null && administrators.Any(user => user.Id == link.AuthUserId))
            {
                logger.LogInformation("初期管理者は作成済みです。認証ID={AuthUserId} 対象人物={PersonCode} 日時={OccurredAtUtc} 結果=変更なし",
                    link.AuthUserId, personCode, time.GetUtcNow());
                return null;
            }
            throw new InvalidOperationException("初期管理者は既に存在します。追加管理者の作成はこのコマンドでは行いません。");
        }
        if (link is not null)
            throw new InvalidOperationException("指定した人物には既にアカウントがあります。自動で管理者に変更しません。");

        var user = new AuthUser { Id = Guid.NewGuid().ToString("N") };
        user.UserName = user.Id;
        RequireSuccess(await users.CreateAsync(user));
        if (!await roles.RoleExistsAsync("Admin"))
            RequireSuccess(await roles.CreateAsync(new IdentityRole("Admin")));
        RequireSuccess(await users.AddToRoleAsync(user, "Admin"));
        context.UserLinks.Add(new AuthUserLink { AuthUserId = user.Id, UserCode = personCode });
        var issued = await AddCodeAsync(user.Id, token);
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        logger.LogInformation("初期管理者を作成しました。認証ID={AuthUserId} 対象人物={PersonCode} 日時={OccurredAtUtc} 結果=成功",
            user.Id, personCode, time.GetUtcNow());
        return issued;
    }

    /// <summary>利用停止中・登録待ちの同じアカウントへコードを再発行する。</summary>
    public async Task<IssuedEnrollmentCode> IssueEnrollmentCodeAsync(int personCode, CancellationToken token = default)
    {
        await EnsureMigratedAsync(token);
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        if (!await context.Set<LegacyPerson>().AnyAsync(person => person.Code == personCode, token))
            throw new InvalidOperationException("指定した人物は登録されていません。");
        var link = await context.UserLinks.SingleOrDefaultAsync(row => row.UserCode == personCode, token)
            ?? throw new InvalidOperationException("指定した人物にはアカウントがありません。");
        var user = await users.FindByIdAsync(link.AuthUserId)
            ?? throw new InvalidOperationException("人物に対応するアカウントを確認できません。");
        if (user.IsEnabled || (await users.GetPasskeysAsync(user)).Count > 0)
            throw new InvalidOperationException("利用中またはパスキーが残るアカウントです。先に全失効が必要です。");
        var issued = await AddCodeAsync(user.Id, token);
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        logger.LogInformation("登録コードを発行しました。認証ID={AuthUserId} 対象人物={PersonCode} 日時={OccurredAtUtc} 結果=成功",
            user.Id, personCode, time.GetUtcNow());
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

    /// <summary>明示的なDB移行が済んでいない場合は管理操作を拒否する。</summary>
    private async Task EnsureMigratedAsync(CancellationToken token)
    {
        if ((await context.Database.GetPendingMigrationsAsync(token)).Any())
            throw new InvalidOperationException("先にmigrate-auth-dbを実行してください。");
    }

    /// <summary>Identityの保存失敗をトランザクション全体の失敗にする。</summary>
    private static void RequireSuccess(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException("認証アカウントの保存に失敗しました。");
    }
}
