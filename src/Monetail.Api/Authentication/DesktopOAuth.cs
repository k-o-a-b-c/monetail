using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Monetail.Api.Persistence;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Monetail.Api.Authentication;

/// <summary>標準ライブラリによる認可コード＋PKCEと、固定期限付きWindowsセッションを構成する。</summary>
public static class DesktopOAuth
{
    /// <summary>APIトークンを短期にし、固定期限内の更新へ切り替える間隔を定める。</summary>
    public static readonly TimeSpan AccessLifetime = TimeSpan.FromMinutes(15);

    /// <summary>OAuthの標準エンドポイント・永続鍵・検証処理を登録する。</summary>
    public static void AddDesktopOAuth(this IServiceCollection services, DesktopOAuthSettings settings, PasskeySettings passkeys)
    {
        services.AddSingleton(settings);
        services.AddSingleton<DesktopOAuthCredentials>();
        services.AddLogging(logging => logging.AddFilter("OpenIddict", LogLevel.Warning));
        services.AddOpenIddict().AddServer(options =>
        {
            options.SetIssuer(passkeys.Origin);
            options.SetAuthorizationEndpointUris("/connect/authorize").SetTokenEndpointUris("/connect/token");
            options.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow().RequireProofKeyForCodeExchange();
            options.RegisterScopes(DesktopOAuthModel.Scope);
            options.SetAuthorizationCodeLifetime(TimeSpan.FromMinutes(1));
            options.SetAccessTokenLifetime(AccessLifetime);
            options.DisableSlidingRefreshTokenExpiration();
            options.EnableAuthorizationRequestCaching();
            options.UseAspNetCore().EnableAuthorizationEndpointPassthrough().EnableTokenEndpointPassthrough();
            options.Configure(server =>
            {
                server.CodeChallengeMethods.Clear();
                server.CodeChallengeMethods.Add(CodeChallengeMethods.Sha256);
                server.ResponseModes.Clear();
                server.ResponseModes.Add(ResponseModes.Query);
                server.Scopes.Clear();
                server.Scopes.Add(DesktopOAuthModel.Scope);
                server.Scopes.Add(Scopes.OfflineAccess);
            });
        }).AddValidation(options =>
        {
            options.UseLocalServer();
            options.UseAspNetCore();
            options.EnableTokenEntryValidation();
            options.EnableAuthorizationEntryValidation();
            options.AddAudiences(DesktopOAuthModel.Scope);
        });
        services.AddOptions<OpenIddictServerOptions>().Configure<DesktopOAuthCredentials, TimeProvider>((options, keys, time) =>
        {
            options.TimeProvider = time;
            options.SigningCredentials.Add(new SigningCredentials(new X509SecurityKey(keys.Signing), SecurityAlgorithms.RsaSha256));
            options.EncryptionCredentials.Add(new EncryptingCredentials(new X509SecurityKey(keys.Encryption),
                SecurityAlgorithms.RsaOAEP, SecurityAlgorithms.Aes256CbcHmacSha512));
        });
        services.AddOptions<OpenIddictServerOptions>().ValidateOnStart();
        services.AddOptions<OpenIddictValidationOptions>().Configure<TimeProvider>((options, time) => options.TimeProvider = time);
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, DesktopTokenHandler>(DesktopTokenHandler.SchemeName, _ => { });
    }

    /// <summary>OAuthの保存先を事前確認し、標準処理中のDB障害も秘密情報なしの503へ変換する。</summary>
    public static async Task StoreBoundary(HttpContext http, RequestDelegate next)
    {
        try
        {
            await DatabaseInitializer.EnsureReadyAsync(http.RequestServices.GetRequiredService<ApiDbContext>(), http.RequestAborted);
            await next(http);
        }
        catch (Exception exception) when (exception is SqliteException or DbUpdateException or InvalidOperationException)
        {
            if (http.Response.HasStarted) throw;
            http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("DesktopOAuth")
                .LogWarning("OAuthの保存先を利用できません。エラー種類={ErrorType}", exception.GetType().Name);
            http.Response.Clear();
            http.Response.StatusCode = 503;
        }
    }

    /// <summary>OAuthに必要な公開経路を判定する。</summary>
    public static bool IsPath(PathString path) => path.StartsWithSegments("/connect") || path.StartsWithSegments("/.well-known");

    /// <summary>検証済みの認可要求と、標準検証を通ったコード／更新トークンの交換を受け付ける。</summary>
    public static void MapDesktopOAuth(this WebApplication app)
    {
        app.MapGet("/connect/authorize", AuthorizeAsync).AllowAnonymous().RequireRateLimiting("passkey-authentication");
        app.MapPost("/connect/token", ExchangeAsync).AllowAnonymous().RequireRateLimiting("passkey-authentication");
    }

    /// <summary>ブラウザーの本人を確認し、元の期限を引き継ぐ独立したWindowsセッションを作る。</summary>
    private static async Task<IResult> AuthorizeAsync(HttpContext http, ApiDbContext database, SessionValidator validator, TimeProvider time)
    {
        var request = http.GetOpenIddictServerRequest()!;
        if (!request.HasScope(DesktopOAuthModel.Scope) || request.GetScopes().Any(scope =>
            scope != DesktopOAuthModel.Scope && scope != Scopes.OfflineAccess)) return Denied(Errors.InvalidScope);
        var browser = await http.AuthenticateAsync(SessionCookieEvents.Scheme);
        if (!browser.Succeeded)
        {
            if (http.Items.TryGetValue(SessionCookieEvents.FailureKey, out var failure) && (int)failure! == 503)
                return Results.StatusCode(503);
            var target = http.Request.PathBase + http.Request.Path + http.Request.QueryString;
            return Results.Redirect(QueryHelpers.AddQueryString("/auth/login", "returnUrl", target));
        }
        var userId = browser.Principal!.FindFirstValue(ClaimTypes.NameIdentifier);
        var sourceId = browser.Principal.FindFirstValue(SessionCookieEvents.IdClaim);
        await using var transaction = await database.Database.BeginTransactionAsync(http.RequestAborted);
        var current = await validator.FindAsync(userId, sourceId, http.RequestAborted);
        if (current is null) return Denied(Errors.AccessDenied);
        var now = time.GetUtcNow().UtcDateTime;
        var id = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var session = new AuthSession
        {
            IdHash = SessionCookieEvents.Hash(id)!, AuthUserId = current.Session.AuthUserId,
            CreatedAtUtc = now, ExpiresAtUtc = current.Session.ExpiresAtUtc, VerifiedAtUtc = current.Session.VerifiedAtUtc
        };
        database.Sessions.Add(session);
        await database.SaveChangesAsync(http.RequestAborted);
        if (await validator.FindAsync(userId, sourceId, http.RequestAborted) is null) return Denied(Errors.AccessDenied);
        await transaction.CommitAsync(http.RequestAborted);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(Claims.Subject, session.AuthUserId),
            new Claim(SessionCookieEvents.IdClaim, id).SetDestinations(Destinations.AccessToken)
        ], "OAuth"));
        principal.SetScopes(request.GetScopes());
        principal.SetResources(DesktopOAuthModel.Scope);
        SetLifetimes(principal, session, time);
        return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>標準の単回コード・PKCE・更新トークン検証後に、固定期限と失効を再確認する。</summary>
    private static async Task<IResult> ExchangeAsync(HttpContext http, SessionValidator validator, TimeProvider time)
    {
        var result = await http.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        if (!result.Succeeded || result.Principal is null) return Denied();
        var principal = result.Principal;
        var current = await validator.FindAsync(principal.FindFirstValue(Claims.Subject),
            principal.FindFirstValue(SessionCookieEvents.IdClaim), http.RequestAborted);
        if (current is null) return Denied();
        SetLifetimes(principal, current.Session, time);
        return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>アクセストークンを短期にし、更新でも元セッションの絶対期限を超えさせない。</summary>
    private static void SetLifetimes(ClaimsPrincipal principal, AuthSession session, TimeProvider time)
    {
        var remaining = session.ExpiresAtUtc - time.GetUtcNow().UtcDateTime;
        principal.SetAccessTokenLifetime(remaining < AccessLifetime ? remaining : AccessLifetime);
        principal.SetRefreshTokenLifetime(remaining);
    }

    /// <summary>トークンの失効理由やアカウント状態を明かさず交換を拒否する。</summary>
    private static IResult Denied(string error = Errors.InvalidGrant) => Results.Forbid(new AuthenticationProperties(new Dictionary<string, string?>
    {
        [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "認証をやり直してください。"
    }), [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
}
