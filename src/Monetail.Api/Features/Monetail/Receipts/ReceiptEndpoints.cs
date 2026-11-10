using Microsoft.AspNetCore.DataProtection;
using Monetail.Api.Authentication;
using Monetail.Api.Features.Monetail.Ledgers;

namespace Monetail.Api.Features.Monetail.Receipts;

public static class ReceiptEndpoints
{
    public static void Map(WebApplication app)
    {
        var routes = app.MapGroup("/ledgers/{ledgerId}/receipts").RequireAuthorization()
            .AddEndpointFilter<AuthRequestFilter>().AddEndpointFilter<LedgerApiFilter>()
            .AddEndpointFilter(async (invocation, next) =>
            {
                try { return await next(invocation); }
                catch (LedgerApiFault error)
                {
                    // 共通の権限情報も、このAPI境界ではD形式で返す。他の家計簿APIは変更しない。
                    if (error.Permissions is { MyPersonId: not null } permission)
                        error.WithPermissions(permission with { MyPersonId = Guid.Parse(permission.MyPersonId).ToString("D") });
                    throw;
                }
            });
        routes.MapGet("", async (string ledgerId, HttpContext http, LedgerManagementService service, IDataProtectionProvider protection) =>
            Results.Ok(await service.ListReceiptsAsync(ReceiptPayload.Uuid(ledgerId, "ledgerId"), http.Request.Query, protection, Current(http), http.RequestAborted)))
            .RequireRateLimiting(LedgerRateLimits.ReadPolicy);
        routes.MapGet("/{receiptId}", async (string ledgerId, string receiptId, HttpContext http, LedgerManagementService service) =>
            Results.Ok(await service.GetReceiptAsync(ReceiptPayload.Uuid(ledgerId, "ledgerId"), ReceiptPayload.Uuid(receiptId, "receiptId"), Current(http), http.RequestAborted)))
            .RequireRateLimiting(LedgerRateLimits.ReadPolicy);
        routes.MapPost("", async (string ledgerId, HttpContext http, LedgerManagementService service) =>
            Reply(http, ledgerId, await service.WriteReceiptAsync(ReceiptPayload.Uuid(ledgerId, "ledgerId"), null, await ReceiptPayload.ReadAsync(http), Current(http), http.RequestAborted)))
            .RequireRateLimiting(LedgerRateLimits.WritePolicy);
        routes.MapPut("/{receiptId}", async (string ledgerId, string receiptId, HttpContext http, LedgerManagementService service) =>
            Reply(http, ledgerId, await service.WriteReceiptAsync(ReceiptPayload.Uuid(ledgerId, "ledgerId"), ReceiptPayload.Uuid(receiptId, "receiptId"), await ReceiptPayload.ReadAsync(http), Current(http), http.RequestAborted)))
            .RequireRateLimiting(LedgerRateLimits.WritePolicy);
        routes.MapDelete("/{receiptId}", async (string ledgerId, string receiptId, HttpContext http, LedgerManagementService service) =>
            Reply(http, ledgerId, await service.DeleteReceiptAsync(ReceiptPayload.Uuid(ledgerId, "ledgerId"), ReceiptPayload.Uuid(receiptId, "receiptId"), await ReceiptPayload.ReadAsync(http), Current(http), http.RequestAborted)))
            .RequireRateLimiting(LedgerRateLimits.WritePolicy);
    }
    private static CurrentSession Current(HttpContext http) => (CurrentSession)http.Items[typeof(CurrentSession)]!;
    private static IResult Reply(HttpContext http, string ledger, object result)
    {
        var saved = (ReceiptWriteResult)result;
        if (saved.Status == 201) http.Response.Headers.Location = $"/ledgers/{Guid.Parse(ledger):D}/receipts/{Guid.ParseExact(saved.ReceiptId, "N"):D}";
        return saved.Status == 201 ? Results.StatusCode(201) : Results.NoContent();
    }
}
