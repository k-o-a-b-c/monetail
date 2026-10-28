using System.Reflection;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

namespace Monetail.Api.Authentication;

/// <summary>パスキー・Cookie／APIトークン認証の共通設定とHTTP境界を構成する。</summary>
public static class PasskeyWeb
{
    /// <summary>パスキー検証・セッション認証・CSRF・既知プロキシ・試行回数制限を登録する。</summary>
    public static void AddPasskeyWeb(this IServiceCollection services, PasskeySettings settings, bool desktopOAuthEnabled = false)
    {
        services.AddSingleton(settings);
        services.AddSingleton<EnrollmentChallenges>();
        services.AddScoped<PasskeyEnrollmentService>();
        services.AddSingleton<LoginChallenges>();
        services.AddScoped<PasskeyLoginService>();
        services.AddSingleton<ReauthenticationChallenges>();
        services.AddScoped<PasskeyReauthenticationService>();
        services.AddSingleton<PasskeyAdditionChallenges>();
        services.AddSingleton<AuthenticationAttemptLimiter>();
        services.AddScoped<PasskeyManagementService>();
        services.AddScoped<IAuthorizationHandler, RecentPasskeyHandler>();
        services.AddScoped<SessionCookieEvents>();
        services.AddScoped<SessionValidator>();
        services.AddAuthentication(DesktopTokenHandler.RequestScheme)
            .AddPolicyScheme(DesktopTokenHandler.RequestScheme, null, options => options.ForwardDefaultSelector = http =>
                desktopOAuthEnabled && !string.IsNullOrEmpty(http.Request.Headers.Authorization)
                    ? DesktopTokenHandler.SchemeName : SessionCookieEvents.Scheme)
            .AddCookie(SessionCookieEvents.Scheme, options =>
        {
            options.Cookie.Name = SessionCookieEvents.CookieName;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Path = "/";
            options.SlidingExpiration = false;
            options.EventsType = typeof(SessionCookieEvents);
        });
        services.AddOptions<CookieAuthenticationOptions>(SessionCookieEvents.Scheme)
            .Configure<TimeProvider>((options, time) => options.TimeProvider = time);
        services.AddAuthorizationBuilder().SetFallbackPolicy(new AuthorizationPolicyBuilder(DesktopTokenHandler.RequestScheme)
            .RequireAuthenticatedUser().Build())
            .AddPolicy(RecentPasskeyRequirement.PolicyName, policy => policy.AddAuthenticationSchemes(DesktopTokenHandler.RequestScheme)
                .RequireAuthenticatedUser().AddRequirements(new RecentPasskeyRequirement()));
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
            // IP枠はOAuthの早期応答も数えられる共通処理で確認する。ここでは同じ要求を重ねて数えない。
            options.AddPolicy("passkey-authentication", _ => RateLimitPartition.GetNoLimiter("authentication-counted"));
        });
    }

    /// <summary>認証・管理・追加保護経路のHTTPS境界と、保護APIの既定認証を構成する。</summary>
    public static void MapPasskeyWeb(this WebApplication app, PasskeySettings settings, params string[] additionalProtectedPaths)
    {
        var desktopOAuthEnabled = app.Services.GetService<DesktopOAuthSettings>() is not null;
        app.UseForwardedHeaders();
        app.Use(async (http, next) =>
        {
            if (http.Request.Path.StartsWithSegments("/auth") || http.Request.Path.StartsWithSegments("/admin") ||
                additionalProtectedPaths.Any(path => http.Request.Path.StartsWithSegments(path)))
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
                if (HttpMethods.IsPost(http.Request.Method) || HttpMethods.IsDelete(http.Request.Method))
                {
                    // Cookie要求のOriginは従来どおり先に確認し、Bearer候補だけを認証後の判定へ回す。
                    if (http.Request.Path != "/connect/token" &&
                        !(desktopOAuthEnabled && !string.IsNullOrEmpty(http.Request.Headers.Authorization)) &&
                        http.Request.Headers.Origin != settings.Origin.GetLeftPart(UriPartial.Authority))
                    {
                        http.Response.StatusCode = 403;
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
        app.UseRouting();
        app.Use((http, next) => http.RequestServices.GetRequiredService<AuthenticationAttemptLimiter>().InvokeAsync(http, next));
        // OAuth標準処理が早期終了する要求も、認証API共通のIP枠と保存先の検証を通す。
        app.UseWhen(http => desktopOAuthEnabled && DesktopOAuth.IsPath(http.Request.Path), branch =>
        {
            branch.Use(DesktopOAuth.StoreBoundary);
        });
        app.UseAuthentication();
        app.Use(async (http, next) =>
        {
            var protectedPath = http.Request.Path.StartsWithSegments("/auth") || http.Request.Path.StartsWithSegments("/admin") ||
                additionalProtectedPaths.Any(path => http.Request.Path.StartsWithSegments(path));
            var write = HttpMethods.IsPost(http.Request.Method) || HttpMethods.IsDelete(http.Request.Method) || HttpMethods.IsPut(http.Request.Method);
            if (protectedPath && write && http.Request.Path != "/connect/token" &&
                http.User.Identity?.AuthenticationType != DesktopTokenHandler.SchemeName &&
                http.Request.Headers.Origin != settings.Origin.GetLeftPart(UriPartial.Authority))
            {
                http.Response.StatusCode = 403;
                return;
            }
            await next(http);
        });
        // Cookie／Bearerを検証してから、確定したアカウント単位の回数制限を判定する。
        app.UseRateLimiter();
        app.Use(async (http, next) =>
        {
            if (http.GetEndpoint()?.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() is null &&
                http.Items.TryGetValue(SessionCookieEvents.FailureKey, out var failure))
            {
                http.Response.StatusCode = (int)failure!;
                return;
            }
            await next(http);
        });
        app.UseAuthorization();
        app.MapGet("/auth/csrf", (HttpContext http, IAntiforgery csrf) =>
            Results.Ok(new { token = csrf.GetAndStoreTokens(http).RequestToken })).AllowAnonymous();
        app.MapPasskeyEnrollment();
        app.MapPasskeyLogin();
        app.MapPasskeyReauthentication();
        app.MapPasskeyManagement();
    }

    /// <summary>埋込済みの認証画面・スクリプトだけを読み込む。</summary>
    internal static string ReadPage(string file)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Monetail.Api.Pages." + file)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
