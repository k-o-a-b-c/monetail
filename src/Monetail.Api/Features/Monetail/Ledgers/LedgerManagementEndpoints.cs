using Monetail.Api.Authentication;

namespace Monetail.Api.Features.Monetail.Ledgers;

/// <summary>人物・参加者・招待の操作を、共通の認証境界へ登録する。</summary>
public static class LedgerManagementEndpoints
{
    public static void MapLedgerManagement(this WebApplication app)
    {
        var routes = app.MapGroup("/ledgers/{ledgerId:guid}").RequireAuthorization()
            .AddEndpointFilter<AuthRequestFilter>().AddEndpointFilter<LedgerApiFilter>();
        routes.MapGet("/categories", async (Guid ledgerId, HttpContext http, LedgerManagementService service) =>
            Reply(await service.GetCategoriesAsync(Key(ledgerId), Current(http), http.RequestAborted))).RequireRateLimiting(LedgerRateLimits.ReadPolicy);
        routes.MapPost("/categories", async (Guid ledgerId, HttpContext http, LedgerManagementService service) =>
            Reply(await service.CreateCategoryAsync(Key(ledgerId), await LedgerPayload.ReadAsync(http, "requestId", "name", "incomeExpense", "taxCategoryCode", "validFrom", "validTo"), Current(http), http.RequestAborted))).RequireRateLimiting(LedgerRateLimits.WritePolicy);
        routes.MapPut("/categories/order", async (Guid ledgerId, HttpContext http, LedgerManagementService service) =>
            Reply(await service.SaveCategoryOrderAsync(Key(ledgerId), await LedgerPayload.ReadAsync(http, "revision", "categoryIds"), Current(http), http.RequestAborted))).RequireRateLimiting(LedgerRateLimits.WritePolicy);
        routes.MapPatch("/categories/{categoryId:guid}", async (Guid ledgerId, Guid categoryId, HttpContext http, LedgerManagementService service) =>
            Reply(await service.UpdateCategoryAsync(Key(ledgerId), Key(categoryId), await LedgerPayload.ReadAsync(http, "revision", "name", "incomeExpense", "taxCategoryCode", "validFrom", "validTo"), Current(http), http.RequestAborted))).RequireRateLimiting(LedgerRateLimits.WritePolicy);
        routes.MapDelete("/categories/{categoryId:guid}", async (Guid ledgerId, Guid categoryId, HttpContext http, LedgerManagementService service) =>
            Reply(await service.DeleteCategoryAsync(Key(ledgerId), Key(categoryId), await LedgerPayload.ReadAsync(http, "revision"), Current(http), http.RequestAborted))).RequireRateLimiting(LedgerRateLimits.WritePolicy);
        routes.MapGet("/payment-methods", async (Guid ledgerId, HttpContext http, LedgerManagementService service) =>
            Reply(await service.GetPaymentMethodsAsync(Key(ledgerId), Current(http), http.RequestAborted))).RequireRateLimiting(LedgerRateLimits.ReadPolicy);
        routes.MapPost("/payment-methods", async (Guid ledgerId, HttpContext http, LedgerManagementService service) =>
            Reply(await service.CreatePaymentMethodAsync(Key(ledgerId), await LedgerPayload.ReadAsync(http, "requestId", "ownerPersonId", "name", "validFrom", "validTo"), Current(http), http.RequestAborted))).RequireRateLimiting(LedgerRateLimits.WritePolicy);
        routes.MapPut("/payment-methods/order", async (Guid ledgerId, HttpContext http, LedgerManagementService service) =>
            Reply(await service.SavePaymentOrderAsync(Key(ledgerId), await LedgerPayload.ReadAsync(http, "ownerPersonId", "revision", "paymentMethodIds"), Current(http), http.RequestAborted))).RequireRateLimiting(LedgerRateLimits.WritePolicy);
        routes.MapPatch("/payment-methods/{paymentMethodId:guid}", async (Guid ledgerId, Guid paymentMethodId, HttpContext http, LedgerManagementService service) =>
            Reply(await service.UpdatePaymentMethodAsync(Key(ledgerId), Key(paymentMethodId), await LedgerPayload.ReadAsync(http, "revision", "name", "validFrom", "validTo"), Current(http), http.RequestAborted))).RequireRateLimiting(LedgerRateLimits.WritePolicy);
        routes.MapDelete("/payment-methods/{paymentMethodId:guid}", async (Guid ledgerId, Guid paymentMethodId, HttpContext http, LedgerManagementService service) =>
            Reply(await service.DeletePaymentMethodAsync(Key(ledgerId), Key(paymentMethodId), await LedgerPayload.ReadAsync(http, "revision"), Current(http), http.RequestAborted))).RequireRateLimiting(LedgerRateLimits.WritePolicy);
        routes.MapGet("/people", async (Guid ledgerId, HttpContext http, LedgerManagementService service) =>
            Reply(await service.GetPeopleAsync(Key(ledgerId), Current(http), http.RequestAborted))).RequireRateLimiting(LedgerRateLimits.ReadPolicy);
        routes.MapPost("/people", async (Guid ledgerId, HttpContext http, LedgerManagementService service) =>
            Reply(await service.CreatePersonAsync(Key(ledgerId), await LedgerPayload.ReadAsync(http, "requestId", "name", "validFrom", "validTo"), Current(http), http.RequestAborted)))
            .RequireRateLimiting(LedgerRateLimits.WritePolicy);
        routes.MapPatch("/people/{personId:guid}", async (Guid ledgerId, Guid personId, HttpContext http, LedgerManagementService service) =>
            Reply(await service.UpdatePersonAsync(Key(ledgerId), Key(personId), await LedgerPayload.ReadAsync(http, "revision", "name", "validFrom", "validTo"), Current(http), http.RequestAborted)))
            .RequireRateLimiting(LedgerRateLimits.WritePolicy);
        routes.MapDelete("/people/{personId:guid}", async (Guid ledgerId, Guid personId, HttpContext http, LedgerManagementService service) =>
            Reply(await service.DeletePersonAsync(Key(ledgerId), Key(personId), await LedgerPayload.ReadAsync(http, "revision"), Current(http), http.RequestAborted)))
            .RequireRateLimiting(LedgerRateLimits.WritePolicy);
        routes.MapPut("/people/order", async (Guid ledgerId, HttpContext http, LedgerManagementService service) =>
            Reply(await service.SaveOrderAsync(Key(ledgerId), await LedgerPayload.ReadAsync(http, "revision", "personIds"), Current(http), http.RequestAborted)))
            .RequireRateLimiting(LedgerRateLimits.WritePolicy);
        routes.MapGet("/members", async (Guid ledgerId, HttpContext http, LedgerManagementService service) =>
            Reply(await service.GetMembersAsync(Key(ledgerId), Current(http), http.RequestAborted))).RequireRateLimiting(LedgerRateLimits.ReadPolicy);
        routes.MapGet("/members/{accountId}/edit", async (Guid ledgerId, string accountId, HttpContext http, LedgerManagementService service) =>
            Reply(await service.GetMemberEditAsync(Key(ledgerId), accountId, Current(http), http.RequestAborted))).RequireRateLimiting(LedgerRateLimits.ReadPolicy);
        routes.MapPatch("/members/{accountId}", async (Guid ledgerId, string accountId, HttpContext http, LedgerManagementService service) =>
            Reply(await service.UpdateMemberAsync(Key(ledgerId), accountId, await LedgerPayload.ReadAsync(http, "revision", "role", "personId"), Current(http), http.RequestAborted)))
            .RequireRateLimiting(LedgerRateLimits.WritePolicy);
        routes.MapDelete("/members/{accountId}", async (Guid ledgerId, string accountId, HttpContext http, LedgerManagementService service) =>
            Reply(await service.ExcludeMemberAsync(Key(ledgerId), accountId, await LedgerPayload.ReadAsync(http, "revision"), Current(http), http.RequestAborted)))
            .RequireRateLimiting(LedgerRateLimits.WritePolicy);
        routes.MapGet("/invitations", async (Guid ledgerId, HttpContext http, LedgerManagementService service) =>
            Reply(await service.GetInvitationsAsync(Key(ledgerId), Current(http), http.RequestAborted))).RequireRateLimiting(LedgerRateLimits.ReadPolicy);
        routes.MapPost("/invitations", async (Guid ledgerId, HttpContext http, LedgerManagementService service) =>
            Reply(await service.CreateInvitationAsync(Key(ledgerId), await LedgerPayload.ReadAsync(http, "requestId"), Current(http), http.RequestAborted)))
            .RequireRateLimiting(LedgerRateLimits.WritePolicy);
        routes.MapDelete("/invitations/{invitationId:guid}", async (Guid ledgerId, Guid invitationId, HttpContext http, LedgerManagementService service) =>
            Reply(await service.RevokeInvitationAsync(Key(ledgerId), Key(invitationId), Current(http), http.RequestAborted))).RequireRateLimiting(LedgerRateLimits.WritePolicy);

        var receive = app.MapGroup("/ledger-invitations").RequireAuthorization()
            .AddEndpointFilter<AuthRequestFilter>().AddEndpointFilter<LedgerApiFilter>();
        receive.MapPost("/preview", async (HttpContext http, LedgerManagementService service) =>
            Reply(await service.PreviewInvitationAsync((await LedgerPayload.ReadAsync(http, "token")).Text("token"), Current(http), http.RequestAborted)))
            .RequireRateLimiting(LedgerRateLimits.ReadPolicy);
        receive.MapPost("/accept", async (HttpContext http, LedgerManagementService service) =>
            Reply(await service.AcceptInvitationAsync((await LedgerPayload.ReadAsync(http, "token")).Text("token"), Current(http), http.RequestAborted)))
            .RequireRateLimiting(LedgerRateLimits.WritePolicy);
    }

    private static string Key(Guid key) => key.ToString("N");
    private static CurrentSession Current(HttpContext http) => (CurrentSession)http.Items[typeof(CurrentSession)]!;
    private static IResult Reply(object value) => value is LedgerCreatedBody created
        ? Results.Json(created.Body, statusCode: created.Replayed ? 200 : 201) : Results.Ok(value);
}
