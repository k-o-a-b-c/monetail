using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;

namespace Monetail.Api.Authentication;

/// <summary>標準のトークン検証後に、現在のセッション・アカウント・役割を検証する。</summary>
public sealed class DesktopTokenHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, SessionValidator validator)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Monetail.Desktop";
    public const string RequestScheme = "Monetail.Request";

    /// <summary>Bearerヘッダーだけを受け付け、Cookieへの認証切替を許さず失効を毎回確認する。</summary>
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        try
        {
            if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var header) ||
                !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(header.Parameter))
                return AuthenticateResult.Fail("APIトークンを確認できません。");
            var result = await Context.AuthenticateAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
            if (!result.Succeeded || result.Principal is null || !result.Principal.HasScope(DesktopOAuthModel.Scope))
                return AuthenticateResult.Fail("APIトークンを確認できません。");
            var id = result.Principal.FindFirstValue(OpenIddictConstants.Claims.Subject);
            var sessionId = result.Principal.FindFirstValue(SessionCookieEvents.IdClaim);
            var current = await validator.FindAsync(id, sessionId, Context.RequestAborted);
            if (current is null) return AuthenticateResult.Fail("セッションを確認できません。");
            var principal = await validator.PrincipalAsync(current, sessionId!, SchemeName, Context.RequestAborted);
            Context.Items[typeof(CurrentSession)] = current;
            return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
        }
        catch (Exception exception) when (exception is SqliteException or DbUpdateException or InvalidOperationException)
        {
            Logger.LogWarning("APIセッションを確認できません。エラー種類={ErrorType}", exception.GetType().Name);
            Context.Items[SessionCookieEvents.FailureKey] = 503;
            return AuthenticateResult.Fail("認証処理を利用できません。");
        }
    }

    /// <summary>APIクライアントへ画面転送せず401を返す。</summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }
}
