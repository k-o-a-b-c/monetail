using Microsoft.EntityFrameworkCore;
using Monetail.Api.Authentication;
using Monetail.Api.Persistence;

namespace Monetail.Api.Features.Accounts;

/// <summary>全体管理者へ返すアカウントの識別子・表示名・利用可否を保持する。</summary>
public sealed record AdminAccountSummary(string AuthUserId, string? DisplayName, bool IsEnabled);

/// <summary>登録済みアカウントの一覧を読み込む。</summary>
public sealed class AdminAccountService(ApiDbContext context)
{
    /// <summary>登録待ち・停止中を含む全アカウントを、安定した認証ID順で取得する。</summary>
    public Task<AdminAccountSummary[]> ListAsync(CancellationToken token) => context.Users.AsNoTracking()
        .OrderBy(user => user.Id)
        .Select(user => new AdminAccountSummary(user.Id, user.DisplayName, user.IsEnabled)).ToArrayAsync(token);
}
