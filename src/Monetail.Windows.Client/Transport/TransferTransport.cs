using System.Net;
using Monetail.Windows.Client.Ledgers;

namespace Monetail.Windows.Client.Transport;

public sealed record TransferSummaryInfo(Guid TransferId, DateTime TransactionAt, string Name, Guid FromPaymentMethodId, Guid ToPaymentMethodId, int Amount, string? Memo);
public sealed record TransferListInfo(TransferSummaryInfo[] Transfers, string? NextCursor);
public sealed record TransferInfo(Guid TransferId, DateTime TransactionAt, string Name, Guid FromPaymentMethodId, Guid ToPaymentMethodId, int Amount, string? Memo, Guid Revision);
public sealed record TransferSuggestionsInfo(string[] Suggestions, bool HasMore);
public sealed record TransferAutofillInfo(Guid? FromPaymentMethodId, Guid? ToPaymentMethodId);

public sealed class TransferRequestException(HttpStatusCode status, string? code, string? field, PeoplePermissions? permissions = null, TransferInfo? latestTransfer = null)
    : Exception("振替の操作を完了できませんでした。")
{
    public HttpStatusCode StatusCode { get; } = status;
    public string? Code { get; } = code;
    public string? Field { get; } = field;
    public PeoplePermissions? Permissions { get; } = permissions;
    public TransferInfo? LatestTransfer { get; } = latestTransfer;
}

public sealed partial class MonetailApiClient
{
    public async Task<TransferListInfo> ListTransfersAsync(string access, Guid ledgerId, TransferListQuery query, CancellationToken token = default)
    {
        var page = await SendTransferAsync<TransferPagePayload>($"{TransferPath(ledgerId)}/transfers?{query.Query}", access, token, stream: true);
        if (page.Transfers is null || page.Transfers.Select(row => row.TransferId).Distinct().Count() != page.Transfers.Count ||
            query.FocusTransferId is Guid focus && (page.Transfers.Count == 0 || page.Transfers[0].TransferId != focus)) throw new ApiConnectionException();
        return new(page.Transfers.Select(row => row with { Memo = CheckTransfer(row.Name, row.FromPaymentMethodId, row.ToPaymentMethodId, row.Amount, row.Memo) }).ToArray(), page.NextCursor);
    }
    public async Task<TransferInfo> GetTransferAsync(string access, Guid ledgerId, Guid transferId, CancellationToken token = default)
    {
        TransferQueries.Id(transferId, "transferId");
        var row = await SendTransferAsync<TransferInfo>($"{TransferPath(ledgerId)}/transfers/{transferId:D}", access, token, stream: true);
        return ValidateTransfer(row, transferId);
    }
    public async Task<TransferSuggestionsInfo> GetTransferSuggestionsAsync(string access, Guid ledgerId, TransferSuggestionQuery query, CancellationToken token = default)
    {
        var suffix = query.Query;
        var result = await SendTransferAsync<TransferSuggestionsPayload>($"{TransferPath(ledgerId)}/transfer-suggestions" + (suffix.Length == 0 ? "" : "?" + suffix), access, token);
        if (result.Suggestions is null || result.Suggestions.Distinct(StringComparer.Ordinal).Count() != result.Suggestions.Count) throw new ApiConnectionException();
        return new(result.Suggestions.ToArray(), result.HasMore);
    }
    public async Task<TransferAutofillInfo> GetTransferAutofillAsync(string access, Guid ledgerId, TransferAutofillQuery query, CancellationToken token = default)
    {
        var result = await SendTransferAsync<TransferAutofillInfo>($"{TransferPath(ledgerId)}/transfer-autofill?{query.Query}", access, token);
        if (result.FromPaymentMethodId is null != (result.ToPaymentMethodId is null) ||
            result.FromPaymentMethodId is Guid from && (from == result.ToPaymentMethodId || query.FromPaymentMethodId is Guid requested && from != requested)) throw new ApiConnectionException();
        return result;
    }
    private Task<T> SendTransferAsync<T>(string path, string access, CancellationToken token, bool stream = false) where T : class =>
        SendResponseAsync<T>(HttpMethod.Get, path, access, null, false, token,
            stream ? TransferJson.ReadAsync<T> : async (content, ct) => TransferJson.Read<T>(await ReadBoundedAsync(content, ct)),
            failure: ReadTransferFailure, includeStoreFailure: true, successStatus: HttpStatusCode.OK);
    private static string TransferPath(Guid ledgerId) { TransferQueries.Id(ledgerId, "ledgerId"); return $"ledgers/{ledgerId:D}"; }
    private static TransferInfo ValidateTransfer(TransferInfo row, Guid transferId)
    {
        if (row.TransferId != transferId) throw new ApiConnectionException();
        return row with { Memo = CheckTransfer(row.Name, row.FromPaymentMethodId, row.ToPaymentMethodId, row.Amount, row.Memo) };
    }
    private static string? CheckTransfer(string name, Guid from, Guid to, int amount, string? memo)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || from == to || amount <= 0 || memo?.Length > 100 ||
            memo?.IndexOfAny(['\r', '\n', '\v', '\f', '\u0085', '\u2028', '\u2029']) >= 0) throw new ApiConnectionException();
        return memo is "" ? null : memo;
    }
    private static TransferRequestException ReadTransferFailure(HttpStatusCode status, byte[] bytes)
    {
        if (bytes.Length == 0) return new(status, null, null);
        var failure = TransferJson.Read<TransferFailurePayload>(bytes); var permission = failure.Permissions;
        if (permission is not null && (permission.CurrentRole is not (null or "Admin" or "Editor" or "Viewer") ||
            permission.CurrentRole is null && permission.MyPersonId is not null)) throw new ApiConnectionException();
        return new(status, failure.Code, failure.Field, status is HttpStatusCode.Forbidden or HttpStatusCode.NotFound ? permission : null);
    }
}
