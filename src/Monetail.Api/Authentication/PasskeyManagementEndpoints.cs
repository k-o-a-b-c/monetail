using System.Text.Json;

namespace Monetail.Api.Authentication;

/// <summary>本人のパスキー一覧・追加・名前変更・削除に必要なHTTP受付を構成する。</summary>
public static class PasskeyManagementEndpoints
{
    /// <summary>一覧にはログイン、追加・名前変更・削除には直近の本人確認とCSRFを要求する。</summary>
    public static void MapPasskeyManagement(this WebApplication app)
    {
        var group = app.MapGroup("/auth/passkeys").RequireAuthorization();
        group.MapGet("", (HttpContext http, PasskeyManagementService service) => service.ListAsync(http, http.RequestAborted))
            .AddEndpointFilter<AuthRequestFilter>();
        group.MapPost("/options", async (HttpContext http, PasskeyManagementService service) =>
        {
            var json = await service.BeginAsync(http, http.RequestAborted);
            return json is null ? Rejected() : Results.Content(json, "application/json");
        }).RequireAuthorization(RecentPasskeyRequirement.PolicyName).AddEndpointFilter<AuthRequestFilter>()
            .RequireRateLimiting("passkey-authentication");
        group.MapPost("/verify", async (PasskeyEnrollmentEndpoints.AttestationRequest request, HttpContext http, PasskeyManagementService service) =>
        {
            if (request.Credential.ValueKind != JsonValueKind.Object) return Rejected();
            return await service.CompleteAsync(request.Credential.GetRawText(), http, http.RequestAborted)
                ? Results.Ok(new { message = "パスキーを追加しました。" }) : Rejected();
        }).RequireAuthorization(RecentPasskeyRequirement.PolicyName).AddEndpointFilter<AuthRequestFilter>()
            .RequireRateLimiting("passkey-authentication");
        group.MapPatch("/{id}", async (string id, JsonElement request, HttpContext http, PasskeyManagementService service) =>
        {
            if (request.ValueKind != JsonValueKind.Object || request.EnumerateObject().Count() != 1 ||
                !request.TryGetProperty("name", out var value) || value.ValueKind != JsonValueKind.String ||
                value.GetString()?.Trim() is not { Length: > 0 and <= 100 } name)
                return Results.BadRequest(new { field = "name", message = "パスキー名は1〜100文字で入力してください。" });
            return await service.RenameAsync(id, name, http, http.RequestAborted) switch
            {
                PasskeyRenameResult.Renamed => Results.NoContent(),
                PasskeyRenameResult.NotFound => Results.NotFound(new { message = "パスキーを確認できません。" }),
                _ => Rejected()
            };
        }).RequireAuthorization(RecentPasskeyRequirement.PolicyName).AddEndpointFilter<AuthRequestFilter>()
            .RequireRateLimiting("passkey-authentication");
        group.MapDelete("/{id}", async (string id, HttpContext http, PasskeyManagementService service) =>
        {
            return await service.RemoveAsync(id, http, http.RequestAborted) switch
            {
                PasskeyRemovalResult.Removed => Results.NoContent(),
                PasskeyRemovalResult.NotFound => Results.NotFound(new { message = "パスキーを確認できません。" }),
                PasskeyRemovalResult.LastPasskey => Results.Json(new { message = "最後のパスキーは削除できません。" }, statusCode: 409),
                _ => Rejected()
            };
        }).RequireAuthorization(RecentPasskeyRequirement.PolicyName).AddEndpointFilter<AuthRequestFilter>()
            .RequireRateLimiting("passkey-authentication");
    }

    /// <summary>本人の切替や検証失敗を、対象情報を含めない共通応答にする。</summary>
    private static IResult Rejected() => Results.Json(new { message = "操作を完了できませんでした。本人確認をやり直してから操作してください。" }, statusCode: 403);
}
