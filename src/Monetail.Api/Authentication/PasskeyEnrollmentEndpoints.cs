using System.Text.Json;

namespace Monetail.Api.Authentication;

/// <summary>登録画面と、登録コードによるパスキー登録のHTTP受付を接続する。</summary>
public static class PasskeyEnrollmentEndpoints
{
    /// <summary>登録用の経路だけを未認証で公開し、POSTにはCSRF検証を付ける。</summary>
    public static void MapPasskeyEnrollment(this WebApplication app)
    {
        var group = app.MapGroup("/auth").AllowAnonymous();
        group.MapGet("/enroll", () => Results.Content(PasskeyWeb.ReadPage("Enrollment.html"), "text/html; charset=utf-8"));
        group.MapGet("/enroll.js", () => Results.Content(PasskeyWeb.ReadPage("Enrollment.js"), "text/javascript; charset=utf-8"));
        group.MapPost("/enrollment/options", async (EnrollmentRequest request, HttpContext http, PasskeyEnrollmentService service) =>
        {
            var json = await service.BeginAsync(request.Code, http, http.RequestAborted);
            return json is null ? Rejected() : Results.Content(json, "application/json");
        }).AddEndpointFilter<AuthRequestFilter>().RequireRateLimiting("passkey-authentication");
        group.MapPost("/enrollment/verify", async (AttestationRequest request, HttpContext http, PasskeyEnrollmentService service) =>
        {
            if (request.Credential.ValueKind != JsonValueKind.Object) return Rejected();
            return await service.CompleteAsync(request.Credential.GetRawText(), http, http.RequestAborted)
                ? Results.Ok(new { message = "パスキーを登録しました。" }) : Rejected();
        }).AddEndpointFilter<AuthRequestFilter>().RequireRateLimiting("passkey-authentication");
    }

    /// <summary>対象人物を明かさず、登録失敗を同じ応答にする。</summary>
    private static IResult Rejected() => Results.BadRequest(new { message = "登録を完了できませんでした。コードを確認し、登録操作をやり直してください。" });

    public sealed record EnrollmentRequest(string? Code);
    public sealed record AttestationRequest(JsonElement Credential);
}
