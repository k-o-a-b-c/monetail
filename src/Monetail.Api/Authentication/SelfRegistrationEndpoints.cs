namespace Monetail.Api.Authentication;

/// <summary>既存認証境界の内側へ、公開モード限定の匿名自己登録だけを追加する。</summary>
public static class SelfRegistrationEndpoints
{
    /// <summary>JSONのモデル結合前にモードと開始枠を判定し、形式不正も開始試行へ含める。</summary>
    public static async Task Boundary(HttpContext http, RequestDelegate next)
    {
        var operation = http.GetEndpoint()?.Metadata.GetMetadata<RegistrationOperation>();
        if (operation is not null)
        {
            if (http.RequestServices.GetRequiredService<RegistrationSettings>().Mode != RegistrationMode.Public)
            {
                http.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            if (http.Request.Headers.Origin != http.RequestServices.GetRequiredService<PasskeySettings>()
                .Origin.GetLeftPart(UriPartial.Authority))
            {
                http.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            if (operation.StartsRegistration && !http.RequestServices.GetRequiredService<SelfRegistrationAttemptLimiter>()
                .TryAcquire(http.Connection.RemoteIpAddress?.ToString() ?? "unknown"))
            {
                http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                return;
            }
        }
        await next(http);
    }

    public static void MapSelfRegistration(this WebApplication app)
    {
        var group = app.MapGroup("/auth/self-registration").AllowAnonymous();
        group.MapPost("/options", async (SelfRegistrationRequest request, HttpContext http, SelfRegistrationService service) =>
            Response(await service.BeginAsync(request.DisplayName, http)))
            .WithMetadata(new RegistrationOperation(true))
            .AddEndpointFilter<AuthRequestFilter>().RequireRateLimiting("passkey-authentication");
        group.MapPost("/verify", async (HttpContext http, SelfRegistrationService service) =>
            Response(await service.CompleteAsync(http, http.RequestAborted)))
            .WithMetadata(new RegistrationOperation(false))
            .AddEndpointFilter<AuthRequestFilter>().RequireRateLimiting("passkey-authentication");
    }

    /// <summary>URL表記の違いでも、実際に選択された登録経路を識別する。</summary>
    public static bool IsEndpoint(HttpContext http) => http.GetEndpoint()?.Metadata.GetMetadata<RegistrationOperation>() is not null;

    private sealed record RegistrationOperation(bool StartsRegistration);

    private static IResult Response(SelfRegistrationResult result) => result.Status switch
    {
        SelfRegistrationStatus.Forbidden => Results.StatusCode(403),
        SelfRegistrationStatus.RateLimited => Results.StatusCode(429),
        SelfRegistrationStatus.Invalid => Results.BadRequest(new { message = "登録を完了できませんでした。入力を確認し、登録操作をやり直してください。" }),
        _ => result.OptionsJson is { } json ? Results.Content(json, "application/json")
            : Results.Ok(new { message = "登録が完了しました。通常のログインへ進んでください。" })
    };

    public sealed record SelfRegistrationRequest(string? DisplayName);
}
