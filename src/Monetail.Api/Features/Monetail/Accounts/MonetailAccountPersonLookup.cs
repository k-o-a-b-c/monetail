using Microsoft.EntityFrameworkCore;
using Monetail.Api.Authentication;

namespace Monetail.Api.Features.Monetail.Accounts;

/// <summary>Monetailの人物対応と人物マスタから、認証に必要な人物情報を読み込む。</summary>
public sealed class MonetailAccountPersonLookup(AuthDbContext context) : IAccountPersonLookup
{
    /// <summary>同じDBスコープとトランザクションで、対応する人物コードと最新の名前を取得する。</summary>
    public Task<AccountPerson?> FindAsync(string authUserId, CancellationToken token) =>
        context.Database.SqlQuery<AccountPerson>($"""
            SELECT person.code AS UserCode, person.name AS DisplayName
            FROM auth_user_links AS link
            INNER JOIN m_user AS person ON person.code = link.user_code
            WHERE link.auth_user_id = {authUserId}
            """).SingleOrDefaultAsync(token);
}
