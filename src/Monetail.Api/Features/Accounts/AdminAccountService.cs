using Microsoft.EntityFrameworkCore;
using Monetail.Api.Authentication;

namespace Monetail.Api.Features.Accounts;

/// <summary>管理者へ返すアカウント自身の識別子・表示名・利用可否を保持する。</summary>
public sealed record AdminAccountSummary(string AuthUserId, string? DisplayName, bool IsEnabled);

/// <summary>家計簿や人物との対応に依存しないアカウント一覧を読み込む。</summary>
public sealed class AdminAccountService(AuthDbContext context)
{
    /// <summary>登録待ち・停止中を含む全アカウントを、安定した認証ID順で取得する。</summary>
    public Task<AdminAccountSummary[]> ListAsync(CancellationToken token) => context.Users.AsNoTracking()
        .OrderBy(user => user.Id)
        .Select(user => new AdminAccountSummary(user.Id, user.DisplayName, user.IsEnabled)).ToArrayAsync(token);
}
