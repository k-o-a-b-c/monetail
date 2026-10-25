using System.Text.Json;

namespace Monetail.Api.Authentication;

/// <summary>ログイン中の本人だけが使えるパスキー再確認のHTTP受付を構成する。</summary>
public static class PasskeyReauthenticationEndpoints
{
    /// <summary>再確認の開始・検証に、既存セッション・CSRF・共通の試行回数制限を要求する。</summary>
    public static void MapPasskeyReauthentication(this WebApplication app)
    {
        var group = app.MapGroup("/auth/reauthentication").RequireAuthorization()
            .AddEndpointFilter<AuthRequestFilter>().RequireRateLimiting("passkey-authentication");
        group.MapPost("/options", async (HttpContext http, PasskeyReauthenticationService service) =>
        {
            var json = await service.BeginAsync(http, http.RequestAborted);
            return json is null ? Rejected() : Results.Content(json, "application/json");
        });
        group.MapPost("/verify", async (PasskeyLoginEndpoints.AssertionRequest request, HttpContext http,
            PasskeyReauthenticationService service) =>
        {
            if (request.Credential.ValueKind != JsonValueKind.Object) return Rejected();
            return await service.CompleteAsync(request.Credential.GetRawText(), http, http.RequestAborted)
                ? Results.Ok(new { message = "本人を再確認しました。" }) : Rejected();
        });
    }

    /// <summary>別人や無効なパスキーなどの再確認失敗を、同じ応答にする。</summary>
    private static IResult Rejected() => Results.Json(new { message = "本人確認を完了できませんでした。もう一度お試しください。" }, statusCode: 403);
}
