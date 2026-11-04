using Monetail.Api.Authentication;
using System.Text.Json.Serialization;

namespace Monetail.Api.Features.Accounts;

/// <summary>全体管理者によるアカウント一覧・新規発行・登録リンク再発行を受け付ける。</summary>
public static class AdminAccountEndpoints
{
    /// <summary>現在のAdminロールを要求し、発行には直近の本人確認とCSRFも要求する。</summary>
    public static void MapAdminAccounts(this WebApplication app)
    {
        app.MapGet("/admin/account-management", () => Results.Content(PasskeyWeb.ReadPage("AdminAccounts.html"), "text/html; charset=utf-8"))
            .RequireAuthorization(policy => policy.RequireAuthenticatedUser().RequireRole("Admin")).AddEndpointFilter<AuthRequestFilter>();
        app.MapGet("/admin/account-management.js", () => Results.Content(PasskeyWeb.ReadPage("AdminAccounts.js"), "text/javascript; charset=utf-8"))
            .RequireAuthorization(policy => policy.RequireAuthenticatedUser().RequireRole("Admin")).AddEndpointFilter<AuthRequestFilter>();
        app.MapPatch("/admin/accounts/{id}/admin-note", async Task<IResult> (string id, UpdateNoteRequest request, HttpContext http, AdminAccountProvisioningService service) =>
        {
            if (request.AdminNote?.Length > AccountAdminNote.MaxLength) return InvalidNote();
            return await service.UpdateNoteAsync(id, request.AdminNote, http, http.RequestAborted) switch
            {
                AccountNoteUpdateStatus.Updated => Results.NoContent(),
                AccountNoteUpdateStatus.NotFound => Results.NotFound(),
                _ => Results.StatusCode(403)
            };
        }).RequireAuthorization(policy => policy.RequireAuthenticatedUser().RequireRole("Admin"))
            .RequireAuthorization(RecentPasskeyRequirement.PolicyName).AddEndpointFilter<AuthRequestFilter>()
            .RequireRateLimiting("passkey-authentication");
        app.MapDelete("/admin/accounts/{id}/enrollment-invitation", async Task<IResult> (string id, HttpContext http, AdminAccountProvisioningService service) =>
        {
            var status = await service.CancelInvitationAsync(id, http, http.RequestAborted);
            return status switch
            {
                EnrollmentInvitationReissueStatus.Issued => Results.NoContent(),
                _ => Results.StatusCode(403)
            };
        }).RequireAuthorization(policy => policy.RequireAuthenticatedUser().RequireRole("Admin"))
            .RequireAuthorization(RecentPasskeyRequirement.PolicyName).AddEndpointFilter<AuthRequestFilter>()
            .RequireRateLimiting("passkey-authentication");
        app.MapGet("/admin/accounts", (HttpContext http, AdminAccountService service) => service.ListAsync(http.RequestAborted))
            .RequireAuthorization(policy => policy.RequireAuthenticatedUser().RequireRole("Admin"))
            .AddEndpointFilter<AuthRequestFilter>();
        app.MapPost("/admin/accounts", async Task<IResult> (CreateAccountRequest request, HttpContext http, AdminAccountProvisioningService service) =>
        {
            if (request.RequestId == Guid.Empty) return Results.BadRequest(new { message = "要求IDを指定してください。" });
            if (request.AdminNote?.Length > AccountAdminNote.MaxLength) return InvalidNote();
            var result = await service.CreateAsync(request.RequestId, http, http.RequestAborted, request.AdminNote);
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
        app.MapPost("/admin/accounts/{id}/enrollment-invitation", async Task<IResult> (string id, HttpContext http, AdminAccountProvisioningService service) =>
        {
            var result = await service.ReissueInvitationAsync(id, http, http.RequestAborted);
            return result.Status switch
            {
                EnrollmentInvitationReissueStatus.Issued => Results.Ok(result.Invitation),
                EnrollmentInvitationReissueStatus.NotFound => Results.NotFound(new { message = "対象アカウントを確認できません。" }),
                EnrollmentInvitationReissueStatus.NotEligible => Results.Conflict(new { message = "利用中または状態が変わったアカウントです。未登録・パスキーなしの場合だけ再発行できます。" }),
                _ => Results.Json(new { message = "操作を完了できませんでした。本人確認をやり直してください。" }, statusCode: 403)
            };
        }).RequireAuthorization(policy => policy.RequireAuthenticatedUser().RequireRole("Admin"))
            .RequireAuthorization(RecentPasskeyRequirement.PolicyName).AddEndpointFilter<AuthRequestFilter>()
            .RequireRateLimiting("passkey-authentication");
    }

    /// <summary>再送識別子だけを受け付け、未対応の項目の追加指定を拒否する。</summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record CreateAccountRequest([property: JsonRequired] Guid RequestId, string? AdminNote = null);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record UpdateNoteRequest([property: JsonRequired] string? AdminNote);

    private static IResult InvalidNote() => Results.BadRequest(new { message = "招待先メモは100文字以内で入力してください。" });
}
