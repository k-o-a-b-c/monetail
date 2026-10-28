using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Monetail.Api.Persistence;

namespace Monetail.Api.Authentication;

/// <summary>認証と保護APIのCSRF・DB準備を確認し、秘密情報を含めない障害応答を返す。</summary>
public sealed class AuthRequestFilter(IAntiforgery csrf, ApiDbContext database, ILogger<AuthRequestFilter> logger) : IEndpointFilter
{
    /// <summary>CSRFを確認して処理を呼び出し、認証ストア障害時は拒否する。</summary>
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            var method = context.HttpContext.Request.Method;
            if (context.HttpContext.User.Identity?.AuthenticationType != DesktopTokenHandler.SchemeName &&
                !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !HttpMethods.IsOptions(method) && !HttpMethods.IsTrace(method))
                await csrf.ValidateRequestAsync(context.HttpContext);
            await DatabaseInitializer.EnsureReadyAsync(database, context.HttpContext.RequestAborted);
            return await next(context);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest(new { message = "操作を確認できません。画面を更新してやり直してください。" });
        }
        catch (Exception exception) when (exception is SqliteException or DbUpdateException or InvalidOperationException)
        {
            logger.LogWarning("認証処理に失敗しました。エラー種類={ErrorType}", exception.GetType().Name);
            return Results.Json(new { message = "認証処理を利用できません。時間をおいてやり直してください。" }, statusCode: 503);
        }
    }
}
