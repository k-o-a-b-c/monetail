using System.Reflection;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Monetail.Api.Authentication;

/// <summary>同一HTTPSオリジンの登録画面と、CSRF検証付き登録APIを接続する。</summary>
public static class PasskeyEnrollmentEndpoints
{
    private const string FailureMessage = "登録を完了できませんでした。コードを確認し、登録操作をやり直してください。";

    /// <summary>パスキー検証・CSRF・既知プロキシ・試行回数制限を登録する。</summary>
    public static void AddPasskeyEnrollment(this IServiceCollection services, PasskeySettings settings)
    {
        services.AddSingleton(settings);
        services.AddSingleton<EnrollmentChallenges>();
        services.AddScoped<PasskeyEnrollmentService>();
        services.AddScoped<IPasskeyHandler<AuthUser>, PasskeyHandler<AuthUser>>();
        services.AddDataProtection().SetApplicationName("Monetail.Api")
            .PersistKeysToFileSystem(new DirectoryInfo(settings.DataProtectionKeysPath));
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "__Host-Monetail.Csrf";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Path = "/";
        });
        services.Configure<IdentityPasskeyOptions>(options =>
        {
            options.ServerDomain = settings.RpId;
            options.UserVerificationRequirement = "required";
            options.ResidentKeyRequirement = "required";
            options.AttestationConveyancePreference = "none";
            options.ValidateOrigin = context => ValueTask.FromResult(!context.CrossOrigin &&
                string.Equals(context.Origin, settings.Origin.GetLeftPart(UriPartial.Authority), StringComparison.Ordinal));
        });
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            // 空の信頼リストは全プロキシの信頼を意味するため、転送処理自体を無効にする。
            if (settings.TrustedProxies.Length == 0) options.ForwardedHeaders = ForwardedHeaders.None;
            foreach (var address in settings.TrustedProxies) options.KnownProxies.Add(address);
            options.AllowedHosts = [settings.Origin.IdnHost];
        });
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("passkey-enrollment", http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
                }));
        });
    }

    /// <summary>登録関連の経路だけを公開し、HTTPや別オリジンからの操作を拒否する。</summary>
    public static void MapPasskeyEnrollment(this WebApplication app, PasskeySettings settings)
    {
        app.UseForwardedHeaders();
        app.Use(async (http, next) =>
        {
            if (http.Request.Path.StartsWithSegments("/auth"))
            {
                http.Response.Headers.CacheControl = "no-store";
                http.Response.Headers["Referrer-Policy"] = "no-referrer";
                http.Response.Headers["X-Content-Type-Options"] = "nosniff";
                http.Response.Headers["Content-Security-Policy"] = "default-src 'none'; script-src 'self'; connect-src 'self'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
                http.Response.Headers["Permissions-Policy"] = "publickey-credentials-create=(self), publickey-credentials-get=(self)";
                if (!settings.Matches(http.Request))
                {
                    http.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }
                if (HttpMethods.IsPost(http.Request.Method))
                {
                    if (http.Request.Headers.Origin != settings.Origin.GetLeftPart(UriPartial.Authority))
                    {
                        http.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return;
                    }
                    if (http.Request.ContentLength > 65536)
                    {
                        http.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                        return;
                    }
                    var size = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
                    if (size is { IsReadOnly: false }) size.MaxRequestBodySize = 65536;
                }
            }
            await next(http);
        });
        app.UseRateLimiter();
        var group = app.MapGroup("/auth");
        group.MapGet("/enroll", () => Results.Content(ReadPage("Enrollment.html"), "text/html; charset=utf-8"));
        group.MapGet("/enroll.js", () => Results.Content(ReadPage("Enrollment.js"), "text/javascript; charset=utf-8"));
        group.MapGet("/csrf", (HttpContext http, IAntiforgery csrf) =>
            Results.Ok(new { token = csrf.GetAndStoreTokens(http).RequestToken }));
        group.MapPost("/enrollment/options", async (EnrollmentRequest request, HttpContext http,
            PasskeyEnrollmentService service) =>
        {
            var json = await service.BeginAsync(request.Code, http, http.RequestAborted);
            return json is null ? Rejected() : Results.Content(json, "application/json");
        }).AddEndpointFilter<EnrollmentFilter>().RequireRateLimiting("passkey-enrollment");
        group.MapPost("/enrollment/verify", async (AttestationRequest request, HttpContext http,
            PasskeyEnrollmentService service) =>
        {
            if (request.Credential.ValueKind != JsonValueKind.Object) return Rejected();
            return await service.CompleteAsync(request.Credential.GetRawText(), http, http.RequestAborted)
                ? Results.Ok(new { message = "パスキーを登録しました。" }) : Rejected();
        }).AddEndpointFilter<EnrollmentFilter>().RequireRateLimiting("passkey-enrollment");
    }

    /// <summary>埋込済みの登録画面・スクリプトだけを読み込む。</summary>
    private static string ReadPage(string file)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Monetail.Api.Pages." + file)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>無効コードや登録失敗を、対象人物を明かさない同じ応答にする。</summary>
    private static IResult Rejected() => Results.BadRequest(new { message = FailureMessage });

    public sealed record EnrollmentRequest(string? Code);
    public sealed record AttestationRequest(JsonElement Credential);

    /// <summary>CSRFを検証し、DB障害時も秘密情報を含めず登録を拒否する。</summary>
    private sealed class EnrollmentFilter(IAntiforgery csrf, ILogger<EnrollmentFilter> logger) : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            try
            {
                await csrf.ValidateRequestAsync(context.HttpContext);
                return await next(context);
            }
            catch (AntiforgeryValidationException) { return Rejected(); }
            catch (Exception exception) when (exception is SqliteException or DbUpdateException or InvalidOperationException)
            {
                logger.LogWarning("パスキー登録処理に失敗しました。エラー種類={ErrorType}", exception.GetType().Name);
                return Results.Json(new { message = "登録処理を利用できません。時間をおいてやり直してください。" }, statusCode: 503);
            }
        }
    }
}
