using Monetail.Api.Authentication;
using System.Text.Json.Serialization;

namespace Monetail.Api.Features.Monetail.Accounts;

/// <summary>管理者による人物・アカウント一覧と、既存人物への一般アカウント発行を受け付ける。</summary>
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
            var result = await service.CreateAsync(request.RequestId, request.UserCode, http, http.RequestAborted);
            return result.Status switch
            {
                AccountCreationStatus.Created => Results.Json(result.Response, statusCode: 201),
                AccountCreationStatus.Replayed => Results.Ok(result.Response),
                AccountCreationStatus.PersonNotFound => Results.NotFound(new { message = "指定した人物を確認できません。" }),
                AccountCreationStatus.AlreadyExists => Results.Conflict(new { message = "指定した人物には既にアカウントがあります。" }),
                AccountCreationStatus.RequestConflict => Results.Conflict(new { message = "この要求IDは別の発行操作で使用済みです。" }),
                _ => Results.Json(new { message = "操作を完了できませんでした。本人確認をやり直してください。" }, statusCode: 403)
            };
        }).RequireAuthorization(policy => policy.RequireAuthenticatedUser().RequireRole("Admin"))
            .RequireAuthorization(RecentPasskeyRequirement.PolicyName).AddEndpointFilter<AuthRequestFilter>()
            .RequireRateLimiting("passkey-authentication");
    }

    /// <summary>既存人物と再送識別子だけを受け付け、名前やロールの追加指定を拒否する。</summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record CreateAccountRequest([property: JsonRequired] Guid RequestId, [property: JsonRequired] int UserCode);
}
