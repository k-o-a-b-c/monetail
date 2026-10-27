using System.Text.Json;

namespace Monetail.Api.Authentication;

/// <summary>ログイン画面・パスキー認証・本人情報・ログアウトのHTTP受付を構成する。</summary>
public static class PasskeyLoginEndpoints
{
    /// <summary>認証開始は公開し、本人情報とログアウトには有効なセッションを要求する。</summary>
    public static void MapPasskeyLogin(this WebApplication app)
    {
        var group = app.MapGroup("/auth");
        group.MapGet("/login", () => Results.Content(PasskeyWeb.ReadPage("Login.html"), "text/html; charset=utf-8")).AllowAnonymous();
        group.MapGet("/login.js", () => Results.Content(PasskeyWeb.ReadPage("Login.js"), "text/javascript; charset=utf-8")).AllowAnonymous();
        group.MapPost("/login/options", async (LoginRequest request, HttpContext http, PasskeyLoginService service) =>
        {
            var json = await service.BeginAsync(request.TimeZone, http);
            return json is null ? Rejected() : Results.Content(json, "application/json");
        }).AllowAnonymous().AddEndpointFilter<AuthRequestFilter>().RequireRateLimiting("passkey-authentication");
        group.MapPost("/login/verify", async (AssertionRequest request, HttpContext http, PasskeyLoginService service) =>
        {
            if (request.Credential.ValueKind != JsonValueKind.Object) return Rejected();
            return await service.CompleteAsync(request.Credential.GetRawText(), http, http.RequestAborted)
                ? Results.Ok(new { message = "ログインしました。" }) : Rejected();
        }).AllowAnonymous().AddEndpointFilter<AuthRequestFilter>().RequireRateLimiting("passkey-authentication");
        group.MapGet("/me", (HttpContext http) =>
        {
            var current = (CurrentSession)http.Items[typeof(CurrentSession)]!;
            return Results.Ok(new
            {
                authUserId = current.Session.AuthUserId, displayName = current.DisplayName,
                expiresAtUtc = DateTime.SpecifyKind(current.Session.ExpiresAtUtc, DateTimeKind.Utc),
                verifiedAtUtc = DateTime.SpecifyKind(current.Session.VerifiedAtUtc, DateTimeKind.Utc)
            });
        }).RequireAuthorization();
        group.MapPost("/logout", async (HttpContext http, PasskeyLoginService service) =>
        {
            await service.LogoutAsync(http, http.RequestAborted);
            return Results.NoContent();
        }).RequireAuthorization().AddEndpointFilter<AuthRequestFilter>();
    }

    /// <summary>人物やパスキーの有無を明かさず、ログイン失敗を同じ401にする。</summary>
    private static IResult Rejected() => Results.Json(new { message = "ログインできませんでした。パスキーでの操作をやり直してください。" }, statusCode: 401);

    public sealed record LoginRequest(string? TimeZone);
    public sealed record AssertionRequest(JsonElement Credential);
}
