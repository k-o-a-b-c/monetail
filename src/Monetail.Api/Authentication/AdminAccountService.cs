using Microsoft.EntityFrameworkCore;

namespace Monetail.Api.Authentication;

/// <summary>人物一覧へ添える認証アカウントの識別子と利用可否を保持する。</summary>
public sealed record AdminAccountSummary(string AuthUserId, bool IsEnabled);

/// <summary>管理者へ返す人物と、未作成ならnullになるアカウント情報を保持する。</summary>
public sealed record AdminPersonSummary(int UserCode, string DisplayName, AdminAccountSummary? Account);

/// <summary>既存人物と対応する認証アカウントの公開状態だけを読み込む。</summary>
public sealed class AdminAccountService(AuthDbContext context)
{
    /// <summary>アカウント未作成の人物も含め、人物コード順に一覧を取得する。</summary>
    public async Task<AdminPersonSummary[]> ListAsync(CancellationToken token)
    {
        var rows = await context.Database.SqlQuery<PersonAccountRow>($"""
            SELECT person.code AS UserCode, person.name AS DisplayName,
                   account.Id AS AuthUserId, account.IsEnabled AS IsEnabled
            FROM m_user AS person
            LEFT JOIN auth_user_links AS link ON link.user_code = person.code
            LEFT JOIN AspNetUsers AS account ON account.Id = link.auth_user_id
            """).OrderBy(row => row.UserCode).ToArrayAsync(token);
        return rows.Select(row => new AdminPersonSummary(row.UserCode, row.DisplayName,
            row.AuthUserId is null ? null : new AdminAccountSummary(row.AuthUserId, row.IsEnabled!.Value))).ToArray();
    }

    /// <summary>人物とアカウントを1回の参照SQLで読み込むための列を保持する。</summary>
    private sealed record PersonAccountRow(int UserCode, string DisplayName, string? AuthUserId, bool? IsEnabled);
}
