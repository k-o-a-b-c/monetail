using System.Text.Json;
using System.Text.Json.Serialization;

namespace Monetail.Api.Authentication;

/// <summary>登録画面と、登録リンクによるパスキー登録のHTTP受付を接続する。</summary>
public static class PasskeyEnrollmentEndpoints
{
    /// <summary>登録用の経路だけを未認証で公開し、POSTにはCSRF検証を付ける。</summary>
    public static void MapPasskeyEnrollment(this WebApplication app)
    {
        var group = app.MapGroup("/auth").AllowAnonymous();
        group.MapGet("/enroll", () => Results.Content(PasskeyWeb.ReadPage("Enrollment.html"), "text/html; charset=utf-8"));
        group.MapGet("/register", IResult (RegistrationSettings settings) => settings.Mode == RegistrationMode.Public
            ? Results.Content(PasskeyWeb.ReadPage("Enrollment.html").Replace("data-registration=\"invitation\"", "data-registration=\"public\""), "text/html; charset=utf-8")
            : Results.StatusCode(403));
        group.MapGet("/enroll.js", () => Results.Content(PasskeyWeb.ReadPage("Enrollment.js"), "text/javascript; charset=utf-8"));
        group.MapPost("/enrollment/options", async (EnrollmentRequest request, HttpContext http, PasskeyEnrollmentService service) =>
        {
            var json = await service.BeginAsync(request.Token, request.DisplayName, http, http.RequestAborted);
            return json is null ? Rejected() : Results.Content(json, "application/json");
        }).WithMetadata(new BrowserEnrollment()).AddEndpointFilter<AuthRequestFilter>().RequireRateLimiting("passkey-authentication");
        group.MapPost("/enrollment/verify", async (AttestationRequest request, HttpContext http, PasskeyEnrollmentService service) =>
        {
            if (request.Credential.ValueKind != JsonValueKind.Object) return Rejected();
            return await service.CompleteAsync(request.Credential.GetRawText(), http, http.RequestAborted)
                ? Results.Ok(new { message = "パスキーを登録しました。" }) : Rejected();
        }).WithMetadata(new BrowserEnrollment()).AddEndpointFilter<AuthRequestFilter>().RequireRateLimiting("passkey-authentication");
    }

    public static bool IsEndpoint(HttpContext http) => http.GetEndpoint()?.Metadata.GetMetadata<BrowserEnrollment>() is not null;
    private sealed record BrowserEnrollment;

    /// <summary>対象アカウントの情報を明かさず、登録失敗を同じ応答にする。</summary>
    private static IResult Rejected() => Results.BadRequest(new { message = "登録を完了できませんでした。招待リンクを確認し、必要なら発行者へ連絡してください。" });

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record EnrollmentRequest(string? Token, string? DisplayName);
    public sealed record AttestationRequest(JsonElement Credential);
}
