using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Monetail.Windows.Client.Ledgers;

namespace Monetail.Windows.Client.Transport;

public sealed record PaymentMethodInfo(Guid PaymentMethodId, Guid OwnerPersonId, string Name, int DisplayOrder,
    DateOnly? ValidFrom, DateOnly? ValidTo, Guid Revision);
public sealed record PaymentOwnerOrderInfo(Guid PersonId, Guid OrderRevision);
public sealed record PaymentMethodsInfo(PaymentMethodInfo[] PaymentMethods, PaymentOwnerOrderInfo[] OwnerOrderRevisions,
    Guid? MyPersonId, string CurrentRole)
{
    /// <summary>表示用の可否判定。保存時の現在権限はAPIが再検証する。</summary>
    public bool CanManageOwner(Guid ownerPersonId) => OwnerOrderRevisions.Any(p => p.PersonId == ownerPersonId) &&
        (CurrentRole == "Admin" || CurrentRole == "Editor" && MyPersonId == ownerPersonId);
}
public sealed record PaymentMethodCreationInfo(Guid PaymentMethodId, PaymentMethodsInfo State);
public sealed record PaymentMethodOrderInfo(Guid OwnerPersonId, Guid OrderRevision, Guid[] PaymentMethodIds);

public sealed class PaymentMethodRequestException(HttpStatusCode status, string? code, string? field,
    PaymentMethodInfo? latestPaymentMethod = null, PaymentMethodsInfo? latestPaymentMethods = null,
    PaymentMethodOrderInfo? latestOrder = null, PeoplePermissions? permissions = null)
    : Exception("支払方法の操作を完了できませんでした。")
{
    public HttpStatusCode StatusCode { get; } = status;
    public string? Code { get; } = code;
    public string? Field { get; } = field;
    public PaymentMethodInfo? LatestPaymentMethod { get; } = latestPaymentMethod;
    public PaymentMethodsInfo? LatestPaymentMethods { get; } = latestPaymentMethods;
    public PaymentMethodOrderInfo? LatestOrder { get; } = latestOrder;
    public PeoplePermissions? Permissions { get; } = permissions;
}

public sealed partial class MonetailApiClient
{
    private enum PaymentConflict { Collection, Row, Order }
    public async Task<PaymentMethodsInfo> ListPaymentMethodsAsync(string access, Guid ledgerId, CancellationToken token = default)
    {
        using var json = await SendPaymentAsync(HttpMethod.Get, PaymentPath(ledgerId), access, null, PaymentConflict.Collection, token);
        return ReadPayments(json.RootElement);
    }
    public async Task<PaymentMethodCreationInfo> CreatePaymentMethodAsync(string access, Guid ledgerId, PaymentMethodCreationDraft draft, CancellationToken token = default)
    {
        using var json = await SendPaymentAsync(HttpMethod.Post, PaymentPath(ledgerId), access,
            new { draft.RequestId, draft.OwnerPersonId, draft.Input.Name, draft.Input.ValidFrom, draft.Input.ValidTo }, PaymentConflict.Collection, token);
        var state = ReadPayments(json.RootElement);
        var id = ReadUuidProperty(json.RootElement, "paymentMethodId");
        if (!state.PaymentMethods.Any(row => row.PaymentMethodId == id && row.OwnerPersonId == draft.OwnerPersonId)) throw new ApiConnectionException();
        return new(id, state);
    }
    public async Task<PaymentMethodsInfo> UpdatePaymentMethodAsync(string access, Guid ledgerId, PaymentMethodInfo row,
        PaymentMethodChanges changes, CancellationToken token = default)
    {
        CheckPaymentRowIds(row);
        using var json = await SendPaymentAsync(HttpMethod.Patch, $"{PaymentPath(ledgerId)}/{row.PaymentMethodId:N}", access,
            changes.Body(row), PaymentConflict.Row, token, row);
        return ReadPayments(json.RootElement);
    }
    public async Task<PaymentMethodsInfo> DeletePaymentMethodAsync(string access, Guid ledgerId, PaymentMethodInfo row, CancellationToken token = default)
    {
        CheckPaymentRowIds(row);
        using var json = await SendPaymentAsync(HttpMethod.Delete, $"{PaymentPath(ledgerId)}/{row.PaymentMethodId:N}", access,
            new { row.Revision }, PaymentConflict.Row, token, row);
        return ReadPayments(json.RootElement);
    }
    public async Task<PaymentMethodsInfo> SavePaymentMethodOrderAsync(string access, Guid ledgerId, Guid ownerPersonId, Guid revision,
        IReadOnlyList<Guid> paymentMethodIds, CancellationToken token = default)
    {
        PaymentMethodInput.CheckId(ownerPersonId, "ownerPersonId"); PaymentMethodInput.CheckId(revision, "revision");
        var ids = paymentMethodIds.ToArray();
        if (ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length)
            throw new PaymentMethodInputException("paymentMethodIds", "支払方法のIDを重複なく指定してください。");
        using var json = await SendPaymentAsync(HttpMethod.Put, $"{PaymentPath(ledgerId)}/order", access,
            new { ownerPersonId, revision, paymentMethodIds = ids }, PaymentConflict.Order, token, owner: ownerPersonId);
        return ReadPayments(json.RootElement);
    }
    private static void CheckPaymentRowIds(PaymentMethodInfo row)
    {
        PaymentMethodInput.CheckId(row.PaymentMethodId, "paymentMethodId"); PaymentMethodInput.CheckId(row.OwnerPersonId, "ownerPersonId");
        PaymentMethodInput.CheckId(row.Revision, "revision");
    }
    private static string PaymentPath(Guid ledgerId)
    {
        PaymentMethodInput.CheckId(ledgerId, "ledgerId"); return $"ledgers/{ledgerId:N}/payment-methods";
    }
    private async Task<JsonDocument> SendPaymentAsync(HttpMethod method, string path, string access, object? input,
        PaymentConflict conflict, CancellationToken token, PaymentMethodInfo? row = null, Guid? owner = null)
    {
        using var body = input is null ? null : JsonContent.Create(input);
        return Parse(await SendAsync(method, path, access, null, false, token, body,
            (status, bytes) => ReadPaymentFailure(status, bytes, conflict, row, owner)));
    }
    private static PaymentMethodRequestException ReadPaymentFailure(HttpStatusCode status, byte[] bytes,
        PaymentConflict conflict, PaymentMethodInfo? row, Guid? owner)
    {
        if (status is HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.TooManyRequests)
        {
            var error = ReadPeopleFailure(status, bytes, false);
            return new(status, error.Code, error.Field, permissions: error.Permissions);
        }
        using var json = Parse(bytes); var root = json.RootElement; var code = String(root, "code"); var field = String(root, "field");
        if (status != HttpStatusCode.Conflict || code != "revision_conflict") return new(status, code, field);
        if (!root.TryGetProperty("latest", out var latest)) throw new ApiConnectionException();
        if (conflict == PaymentConflict.Collection) return new(status, code, field, latestPaymentMethods: ReadPayments(latest));
        if (conflict == PaymentConflict.Row)
        {
            var value = ReadPayment(latest);
            if (value.PaymentMethodId != row!.PaymentMethodId || value.OwnerPersonId != row.OwnerPersonId) throw new ApiConnectionException();
            return new(status, code, field, latestPaymentMethod: value);
        }
        var order = ReadPaymentOrder(latest);
        if (order.OwnerPersonId != owner) throw new ApiConnectionException();
        return new(status, code, field, latestOrder: order);
    }
    private static PaymentMethodsInfo ReadPayments(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("paymentMethods", out var rows) || rows.ValueKind != JsonValueKind.Array ||
            !json.TryGetProperty("ownerOrderRevisions", out var owners) || owners.ValueKind != JsonValueKind.Array ||
            String(json, "currentRole") is not ("Admin" or "Editor" or "Viewer") || !json.TryGetProperty("myPersonId", out var mine)) throw new ApiConnectionException();
        var payments = rows.EnumerateArray().Select(ReadPayment).ToArray();
        var versions = owners.EnumerateArray().Select(p => p.ValueKind == JsonValueKind.Object
            ? new PaymentOwnerOrderInfo(ReadUuidProperty(p, "personId"), ReadUuidProperty(p, "orderRevision")) : throw new ApiConnectionException()).ToArray();
        Guid? myId = mine.ValueKind == JsonValueKind.Null ? null : ReadUuid(mine);
        var ownerIds = versions.Select(p => p.PersonId).ToHashSet();
        if (ownerIds.Count != versions.Length || payments.Select(p => p.PaymentMethodId).Distinct().Count() != payments.Length ||
            payments.Any(p => !ownerIds.Contains(p.OwnerPersonId)) || myId is not null && !ownerIds.Contains(myId.Value)) throw new ApiConnectionException();
        // API配列順と有効期間外の行・0件人物の版をそのまま保持し、表示時の絞り込みは行わない。
        return new(payments, versions, myId, String(json, "currentRole")!);
    }
    private static PaymentMethodInfo ReadPayment(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object || String(json, "name") is not { Length: > 0 and <= 100 } name || string.IsNullOrWhiteSpace(name) ||
            !json.TryGetProperty("displayOrder", out var order) || order.ValueKind != JsonValueKind.Number || !order.TryGetInt32(out var position) || position < 0)
            throw new ApiConnectionException();
        var from = ReadPersonDate(json, "validFrom"); var to = ReadPersonDate(json, "validTo");
        if (from > to) throw new ApiConnectionException();
        return new(ReadUuidProperty(json, "paymentMethodId"), ReadUuidProperty(json, "ownerPersonId"), name, position, from, to, ReadUuidProperty(json, "revision"));
    }
    private static PaymentMethodOrderInfo ReadPaymentOrder(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("paymentMethodIds", out var rows) || rows.ValueKind != JsonValueKind.Array)
            throw new ApiConnectionException();
        var ids = rows.EnumerateArray().Select(ReadUuid).ToArray();
        if (ids.Distinct().Count() != ids.Length) throw new ApiConnectionException();
        return new(ReadUuidProperty(json, "ownerPersonId"), ReadUuidProperty(json, "orderRevision"), ids);
    }
}
