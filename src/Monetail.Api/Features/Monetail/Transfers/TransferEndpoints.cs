using Microsoft.AspNetCore.DataProtection;
using Monetail.Api.Authentication;
using Monetail.Api.Features.Monetail.Ledgers;
using Monetail.Api.Features.Monetail.Receipts;

namespace Monetail.Api.Features.Monetail.Transfers;

public static class TransferEndpoints
{
    public static void Map(WebApplication app)
    {
        var routes = app.MapGroup("/ledgers/{ledgerId}/transfers").RequireAuthorization()
            .AddEndpointFilter<AuthRequestFilter>().AddEndpointFilter<LedgerApiFilter>()
            .AddEndpointFilter(async (invocation, next) =>
            {
                try { return await next(invocation); }
                catch (LedgerApiFault error)
                {
                    // レシートと同じく、共通エラーの人物IDもAPIではD形式にする。
                    if (error.Permissions is { MyPersonId: not null } permission)
                        error.WithPermissions(permission with { MyPersonId = Guid.Parse(permission.MyPersonId).ToString("D") });
                    throw;
                }
            });
        routes.MapGet("", async (string ledgerId, HttpContext http, LedgerManagementService service, IDataProtectionProvider protection) =>
            Results.Ok(await service.ListTransfersAsync(ReceiptPayload.Uuid(ledgerId, "ledgerId"), http.Request.Query, protection,
                (CurrentSession)http.Items[typeof(CurrentSession)]!, http.RequestAborted)))
            .RequireRateLimiting(LedgerRateLimits.ReadPolicy);
        routes.MapGet("/{transferId}", async (string ledgerId, string transferId, HttpContext http, LedgerManagementService service) =>
            Results.Ok(await service.GetTransferAsync(ReceiptPayload.Uuid(ledgerId, "ledgerId"), ReceiptPayload.Uuid(transferId, "transferId"),
                (CurrentSession)http.Items[typeof(CurrentSession)]!, http.RequestAborted)))
            .RequireRateLimiting(LedgerRateLimits.ReadPolicy);
    }
}
