using Monetail.Api.Authentication;

namespace Monetail.Api.Features.Monetail.Accounts;

/// <summary>管理者による人物・アカウント状態の一覧取得を受け付ける。</summary>
public static class AdminAccountEndpoints
{
    /// <summary>有効なログインと現在のAdminロールを条件に、参照専用の一覧を公開する。</summary>
    public static void MapAdminAccounts(this WebApplication app)
    {
        app.MapGet("/admin/accounts", (HttpContext http, AdminAccountService service) => service.ListAsync(http.RequestAborted))
            .RequireAuthorization(policy => policy.RequireAuthenticatedUser().RequireRole("Admin"))
            .AddEndpointFilter<AuthRequestFilter>();
    }
}
