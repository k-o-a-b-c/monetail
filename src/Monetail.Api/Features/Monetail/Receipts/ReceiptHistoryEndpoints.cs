using Monetail.Api.Authentication;
using Monetail.Api.Features.Monetail.Ledgers;

namespace Monetail.Api.Features.Monetail.Receipts;

public static class ReceiptHistoryEndpoints
{
    public static void Map(WebApplication app)
    {
        var routes = app.MapGroup("/ledgers/{ledgerId}").RequireAuthorization()
            .AddEndpointFilter<AuthRequestFilter>().AddEndpointFilter<LedgerApiFilter>();
        routes.MapGet("/receipt-suggestions", async (string ledgerId, HttpContext http, LedgerManagementService service) =>
            Results.Ok(await service.ReceiptSuggestionsAsync(ReceiptPayload.Uuid(ledgerId, "ledgerId"), http.Request.Query, Current(http), http.RequestAborted)))
            .RequireRateLimiting(LedgerRateLimits.ReadPolicy);
        routes.MapGet("/receipt-autofill", async (string ledgerId, HttpContext http, LedgerManagementService service) =>
            Results.Ok(await service.ReceiptAutofillAsync(ReceiptPayload.Uuid(ledgerId, "ledgerId"), http.Request.Query, Current(http), http.RequestAborted)))
            .RequireRateLimiting(LedgerRateLimits.ReadPolicy);
    }
    private static CurrentSession Current(HttpContext http) => (CurrentSession)http.Items[typeof(CurrentSession)]!;
}
