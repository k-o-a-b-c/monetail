using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Monetail.Api.Authentication;

/// <summary>認証系のPOSTにCSRF検証と、秘密情報を含めない障害応答を適用する。</summary>
public sealed class AuthRequestFilter(IAntiforgery csrf, ILogger<AuthRequestFilter> logger) : IEndpointFilter
{
    /// <summary>CSRFを確認して処理を呼び出し、認証ストア障害時は拒否する。</summary>
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            await csrf.ValidateRequestAsync(context.HttpContext);
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
