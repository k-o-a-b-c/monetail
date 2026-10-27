using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Monetail.Api.Authentication;

namespace Monetail.Api.Features.Monetail.Ledgers;

/// <summary>家計簿の作成と、自分が参加する家計簿の参照を受け付ける。</summary>
public static class LedgerEndpoints
{
    /// <summary>ログイン・CSRF・DB準備確認を共通に要求する家計簿APIを登録する。</summary>
    public static void MapLedgers(this WebApplication app)
    {
        var routes = app.MapGroup("/ledgers").RequireAuthorization().AddEndpointFilter<AuthRequestFilter>();
        routes.MapPost("", async Task<IResult> (CreateLedgerRequest request, HttpContext http, LedgerService service) =>
        {
            var name = request.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length > Ledger.NameMaxLength || request.RequestId == Guid.Empty)
                return Results.BadRequest(new { message = "家計簿名は1〜100文字で入力し、要求IDを指定してください。" });
            var result = await service.CreateAsync(name, request.RequestId, Current(http), http.RequestAborted);
            return result.Status switch
            {
                LedgerCreationStatus.Created => Results.Created($"/ledgers/{result.Response!.LedgerId}", result.Response),
                LedgerCreationStatus.Replayed => Results.Ok(result.Response),
                LedgerCreationStatus.Conflict => Results.Conflict(new { message = "この要求IDは別の作成操作で使用済みです。" }),
                LedgerCreationStatus.NotFound => Results.NotFound(),
                _ => Results.Unauthorized()
            };
        }).RequireRateLimiting(LedgerRateLimits.CreationPolicy);
        routes.MapGet("", async Task<IResult> (HttpContext http, LedgerService service) =>
        {
            var current = Current(http);
            if (!await service.IsCurrentAsync(current, http.RequestAborted)) return Results.Unauthorized();
            var rows = await service.VisibleLedgers(current).ToArrayAsync(http.RequestAborted);
            return await service.IsCurrentAsync(current, http.RequestAborted) ? Results.Ok(rows) : Results.Unauthorized();
        }).RequireRateLimiting(LedgerRateLimits.ReadPolicy);
        routes.MapGet("/{id:guid}", async Task<IResult> (Guid id, HttpContext http, LedgerService service) =>
        {
            var current = Current(http);
            if (!await service.IsCurrentAsync(current, http.RequestAborted)) return Results.Unauthorized();
            var key = id.ToString("N");
            var row = await service.VisibleLedgers(current, key).SingleOrDefaultAsync(http.RequestAborted);
            if (!await service.IsCurrentAsync(current, http.RequestAborted)) return Results.Unauthorized();
            return row is null ? Results.NotFound() : Results.Ok(row);
        }).RequireRateLimiting(LedgerRateLimits.ReadPolicy);
    }

    /// <summary>Cookieの検証で確定した現在のログインを取得する。</summary>
    private static CurrentSession Current(HttpContext http) => (CurrentSession)http.Items[typeof(CurrentSession)]!;

    /// <summary>家計簿名と再送識別子だけを受け付ける。</summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record CreateLedgerRequest(string? Name, Guid RequestId);
}
