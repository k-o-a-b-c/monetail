using Monetail.Api.Authentication;
using System.Text.Json.Serialization;

namespace Monetail.Api.Features.Accounts;

/// <summary>管理者によるアカウント一覧・新規発行・登録コード再発行を受け付ける。</summary>
public static class AdminAccountEndpoints
{
    /// <summary>現在のAdminロールを要求し、発行には直近の本人確認とCSRFも要求する。</summary>
    public static void MapAdminAccounts(this WebApplication app)
    {
        app.MapGet("/admin/accounts", (HttpContext http, AdminAccountService service) => service.ListAsync(http.RequestAborted))
            .RequireAuthorization(policy => policy.RequireAuthenticatedUser().RequireRole("Admin"))
            .AddEndpointFilter<AuthRequestFilter>();
        app.MapPost("/admin/accounts", async Task<IResult> (CreateAccountRequest request, HttpContext http, AdminAccountProvisioningService service) =>
        {
            if (request.RequestId == Guid.Empty) return Results.BadRequest(new { message = "要求IDを指定してください。" });
            var result = await service.CreateAsync(request.RequestId, http, http.RequestAborted);
            return result.Status switch
            {
                AccountCreationStatus.Created => Results.Json(result.Response, statusCode: 201),
                AccountCreationStatus.Replayed => Results.Ok(result.Response),
                AccountCreationStatus.RequestConflict => Results.Conflict(new { message = "この要求IDは別の発行操作で使用済みです。" }),
                _ => Results.Json(new { message = "操作を完了できませんでした。本人確認をやり直してください。" }, statusCode: 403)
            };
        }).RequireAuthorization(policy => policy.RequireAuthenticatedUser().RequireRole("Admin"))
            .RequireAuthorization(RecentPasskeyRequirement.PolicyName).AddEndpointFilter<AuthRequestFilter>()
            .RequireRateLimiting("passkey-authentication");
        app.MapPost("/admin/accounts/{id}/enrollment-code", async Task<IResult> (string id, HttpContext http, AdminAccountProvisioningService service) =>
        {
            var result = await service.ReissueCodeAsync(id, http, http.RequestAborted);
            return result.Status switch
            {
                EnrollmentCodeReissueStatus.Issued => Results.Ok(result.IssuedCode),
                EnrollmentCodeReissueStatus.NotFound => Results.NotFound(new { message = "対象アカウントを確認できません。" }),
                EnrollmentCodeReissueStatus.NotEligible => Results.Conflict(new { message = "利用中または状態が変わったアカウントです。停止中・パスキーなしの場合だけ再発行できます。" }),
                _ => Results.Json(new { message = "操作を完了できませんでした。本人確認をやり直してください。" }, statusCode: 403)
            };
        }).RequireAuthorization(policy => policy.RequireAuthenticatedUser().RequireRole("Admin"))
            .RequireAuthorization(RecentPasskeyRequirement.PolicyName).AddEndpointFilter<AuthRequestFilter>()
            .RequireRateLimiting("passkey-authentication");
    }

    /// <summary>再送識別子だけを受け付け、人物・名前・ロールの追加指定を拒否する。</summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record CreateAccountRequest([property: JsonRequired] Guid RequestId);
}
