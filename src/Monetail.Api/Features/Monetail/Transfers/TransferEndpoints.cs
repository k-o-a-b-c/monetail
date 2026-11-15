using Microsoft.AspNetCore.DataProtection;
using Monetail.Api.Authentication;
using Monetail.Api.Features.Monetail.Ledgers;
using Monetail.Api.Features.Monetail.Receipts;

namespace Monetail.Api.Features.Monetail.Transfers;

public static class TransferEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/ledgers/{ledgerId}/transfer-suggestions", async (string ledgerId, HttpContext http, LedgerManagementService service) =>
            Results.Ok(await service.TransferSuggestionsAsync(ReceiptPayload.Uuid(ledgerId, "ledgerId"), http.Request.Query,
                (CurrentSession)http.Items[typeof(CurrentSession)]!, http.RequestAborted)))
            .RequireAuthorization().AddEndpointFilter<AuthRequestFilter>().AddEndpointFilter<LedgerApiFilter>()
            .RequireRateLimiting(LedgerRateLimits.ReadPolicy);
        app.MapGet("/ledgers/{ledgerId}/transfer-autofill", async (string ledgerId, HttpContext http, LedgerManagementService service) =>
            Results.Ok(await service.TransferAutofillAsync(ReceiptPayload.Uuid(ledgerId, "ledgerId"), http.Request.Query,
                (CurrentSession)http.Items[typeof(CurrentSession)]!, http.RequestAborted)))
            .RequireAuthorization().AddEndpointFilter<AuthRequestFilter>().AddEndpointFilter<LedgerApiFilter>()
            .AddEndpointFilter(WithPublicPermissionIdsAsync).RequireRateLimiting(LedgerRateLimits.ReadPolicy);
        var routes = app.MapGroup("/ledgers/{ledgerId}/transfers").RequireAuthorization()
            .AddEndpointFilter<AuthRequestFilter>().AddEndpointFilter<LedgerApiFilter>()
            .AddEndpointFilter(WithPublicPermissionIdsAsync);
        routes.MapGet("", async (string ledgerId, HttpContext http, LedgerManagementService service, IDataProtectionProvider protection) =>
            Results.Ok(await service.ListTransfersAsync(ReceiptPayload.Uuid(ledgerId, "ledgerId"), http.Request.Query, protection,
                (CurrentSession)http.Items[typeof(CurrentSession)]!, http.RequestAborted)))
            .RequireRateLimiting(LedgerRateLimits.ReadPolicy);
        routes.MapPost("", async (string ledgerId, HttpContext http, LedgerManagementService service) =>
        {
            var ledger = ReceiptPayload.Uuid(ledgerId, "ledgerId");
            var result = (TransferCreatedResult)await service.CreateTransferAsync(ledger,
                await LedgerPayload.ReadAsync(http, TransferCreateInput.Fields), (CurrentSession)http.Items[typeof(CurrentSession)]!, http.RequestAborted);
            http.Response.Headers.Location = $"/ledgers/{Guid.ParseExact(ledger, "N"):D}/transfers/{Guid.ParseExact(result.TransferId, "N"):D}";
            return Results.StatusCode(201);
        }).RequireRateLimiting(LedgerRateLimits.WritePolicy);
        routes.MapPut("/{transferId}", async (string ledgerId, string transferId, HttpContext http, LedgerManagementService service) =>
        {
            await service.UpdateTransferAsync(ReceiptPayload.Uuid(ledgerId, "ledgerId"), ReceiptPayload.Uuid(transferId, "transferId"),
                await LedgerPayload.ReadAsync(http, TransferUpdateInput.Fields), (CurrentSession)http.Items[typeof(CurrentSession)]!, http.RequestAborted);
            return Results.NoContent();
        }).RequireRateLimiting(LedgerRateLimits.WritePolicy);
        routes.MapDelete("/{transferId}", async (string ledgerId, string transferId, HttpContext http, LedgerManagementService service) =>
        {
            await service.DeleteTransferAsync(ReceiptPayload.Uuid(ledgerId, "ledgerId"), ReceiptPayload.Uuid(transferId, "transferId"),
                await LedgerPayload.ReadAsync(http, TransferDeleteInput.Fields), (CurrentSession)http.Items[typeof(CurrentSession)]!, http.RequestAborted);
            return Results.NoContent();
        }).RequireRateLimiting(LedgerRateLimits.WritePolicy);
        routes.MapGet("/{transferId}", async (string ledgerId, string transferId, HttpContext http, LedgerManagementService service) =>
            Results.Ok(await service.GetTransferAsync(ReceiptPayload.Uuid(ledgerId, "ledgerId"), ReceiptPayload.Uuid(transferId, "transferId"),
                (CurrentSession)http.Items[typeof(CurrentSession)]!, http.RequestAborted)))
            .RequireRateLimiting(LedgerRateLimits.ReadPolicy);
    }

    private static async ValueTask<object?> WithPublicPermissionIdsAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        try { return await next(invocation); }
        catch (LedgerApiFault error)
        {
            // 共通エラーの人物IDも、振替APIではD形式にする。
            if (error.Permissions is { MyPersonId: not null } permission)
                error.WithPermissions(permission with { MyPersonId = Guid.Parse(permission.MyPersonId).ToString("D") });
            throw;
        }
    }
}
