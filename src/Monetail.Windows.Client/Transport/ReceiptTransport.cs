using System.Net;
using Monetail.Windows.Client.Ledgers;

namespace Monetail.Windows.Client.Transport;

public sealed record ReceiptSummaryInfo(Guid ReceiptId, DateTime TransactionAt, string ShopName, string? ShopLocation, Guid PaymentMethodId, int TotalAmount, string? Memo);
public sealed record ReceiptListInfo(ReceiptSummaryInfo[] Receipts, string? NextCursor);
public sealed record ReceiptInfo(Guid ReceiptId, DateTime TransactionAt, string ShopName, string? ShopLocation, Guid PaymentMethodId,
    int TaxInclusionMode, string TaxRoundingMode, int? RecurrenceInterval, string? RecurrenceUnit, string? Memo, Guid Revision, ReceiptGroupInfo[] Groups);
public sealed record ReceiptGroupInfo(Guid GroupId, Guid CategoryId, string? Memo, ReceiptDetailInfo[] Details);
public sealed record ReceiptDetailInfo(Guid DetailId, int DisplayOrder, string Name, int UnitPrice, int Quantity, int UnitDiscountAmount,
    decimal DiscountRate, decimal? TaxRate, string ExpenseType, string OccurrenceType, string? Memo, Guid[] PersonIds);
public sealed record ReceiptSuggestionsInfo(string[] Suggestions, bool HasMore);
public sealed record ReceiptAutofillInfo(Guid? PaymentMethodId, int? TaxInclusionMode, string? TaxRoundingMode);

public sealed class ReceiptRequestException(HttpStatusCode status, string? code, string? field, PeoplePermissions? permissions = null, string? reason = null, ReceiptInfo? latestReceipt = null)
    : Exception("レシートの操作を完了できませんでした。")
{
    public HttpStatusCode StatusCode { get; } = status;
    public string? Code { get; } = code;
    public string? Field { get; } = field;
    public PeoplePermissions? Permissions { get; } = permissions;
    public string? Reason { get; } = reason;
    public ReceiptInfo? LatestReceipt { get; } = latestReceipt;
}

public sealed partial class MonetailApiClient
{
    public async Task<ReceiptListInfo> ListReceiptsAsync(string access, Guid ledgerId, ReceiptListQuery query, CancellationToken token = default)
    {
        var page = await SendReceiptAsync<ReceiptPagePayload>($"{ReceiptPath(ledgerId)}/receipts?{query.Query}", access, token, stream: true);
        if (page.Receipts is null || page.Receipts.Select(row => row.ReceiptId).Distinct().Count() != page.Receipts.Count) throw new ApiConnectionException();
        foreach (var row in page.Receipts) CheckReceiptText(row.ShopName, row.ShopLocation);
        return new(page.Receipts.ToArray(), page.NextCursor);
    }
    public async Task<ReceiptInfo> GetReceiptAsync(string access, Guid ledgerId, Guid receiptId, CancellationToken token = default)
    {
        ReceiptQueries.Id(receiptId, "receiptId");
        var receipt = await SendReceiptAsync<ReceiptInfo>($"{ReceiptPath(ledgerId)}/receipts/{receiptId:D}", access, token, stream: true);
        return ValidateReceipt(receipt, receiptId);
    }
    private static ReceiptInfo ValidateReceipt(ReceiptInfo receipt, Guid receiptId)
    {
        if (receipt.ReceiptId != receiptId || receipt.Groups is null || receipt.Groups.Any(g => g is null || g.Details is null) ||
            receipt.Groups.Select(g => g.GroupId).Distinct().Count() != receipt.Groups.Length ||
            receipt.Groups.Select(g => g.CategoryId).Distinct().Count() != receipt.Groups.Length) throw new ApiConnectionException();
        CheckReceiptText(receipt.ShopName, receipt.ShopLocation); CheckReceiptTax(receipt.TaxInclusionMode, receipt.TaxRoundingMode);
        if (receipt.RecurrenceInterval is null != (receipt.RecurrenceUnit is null) || receipt.RecurrenceInterval <= 0 ||
            receipt.RecurrenceUnit is not (null or "Week" or "Month" or "Year")) throw new ApiConnectionException();
        var ids = new HashSet<Guid>();
        foreach (var detail in receipt.Groups.SelectMany(group => group.Details))
        {
            if (detail is null || !ids.Add(detail.DetailId) || detail.DisplayOrder < 0 || detail.Name is not { Length: <= 100 } || detail.Quantity <= 0 ||
                detail.DiscountRate is < 0 or > 100 || detail.ExpenseType is not ("Fixed" or "Variable" or "Excluded") ||
                detail.OccurrenceType is not ("Regular" or "Irregular" or "Temporary") || detail.PersonIds is null ||
                detail.PersonIds.Distinct().Count() != detail.PersonIds.Length) throw new ApiConnectionException();
        }
        return receipt;
    }
    public async Task<ReceiptSuggestionsInfo> GetReceiptSuggestionsAsync(string access, Guid ledgerId, ReceiptSuggestionQuery query, CancellationToken token = default)
    {
        var result = await SendReceiptAsync<ReceiptSuggestionsPayload>($"{ReceiptPath(ledgerId)}/receipt-suggestions?{query.Query}", access, token);
        if (result.Suggestions is null || result.Suggestions.Distinct(StringComparer.Ordinal).Count() != result.Suggestions.Count) throw new ApiConnectionException();
        return new(result.Suggestions.ToArray(), result.HasMore);
    }
    public async Task<ReceiptAutofillInfo> GetReceiptAutofillAsync(string access, Guid ledgerId, ReceiptAutofillQuery query, CancellationToken token = default)
    {
        var result = await SendReceiptAsync<ReceiptAutofillInfo>($"{ReceiptPath(ledgerId)}/receipt-autofill?{query.Query}", access, token);
        if (result.TaxInclusionMode is null != (result.TaxRoundingMode is null)) throw new ApiConnectionException();
        if (result.TaxInclusionMode is int inclusion) CheckReceiptTax(inclusion, result.TaxRoundingMode!);
        return result;
    }
    private Task<T> SendReceiptAsync<T>(string path, string access, CancellationToken token, bool stream = false) where T : class =>
        SendResponseAsync<T>(HttpMethod.Get, path, access, null, false, token,
            stream ? ReceiptJson.ReadAsync<T> : async (content, ct) => ReceiptJson.Read<T>(await ReadBoundedAsync(content, ct)),
            failure: ReadReceiptFailure, includeStoreFailure: true);
    private static string ReceiptPath(Guid ledgerId) { ReceiptQueries.Id(ledgerId, "ledgerId"); return $"ledgers/{ledgerId:D}"; }
    private static void CheckReceiptText(string name, string? location)
    {
        if (name is not { Length: <= 100 } || location?.Length > 100) throw new ApiConnectionException();
    }
    private static void CheckReceiptTax(int inclusion, string rounding)
    {
        if (inclusion is not (0 or 1) || rounding is not ("floor" or "half_up")) throw new ApiConnectionException();
    }
    private static ReceiptRequestException ReadReceiptFailure(HttpStatusCode status, byte[] bytes)
    {
        if (status is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            var error = ReadPeopleFailure(status, bytes, false);
            return new(status, error.Code, error.Field, error.Permissions);
        }
        if (bytes.Length == 0) return new(status, null, null);
        using var json = Parse(bytes);
        return new(status, String(json.RootElement, "code"), String(json.RootElement, "field"),
            reason: status == HttpStatusCode.BadRequest ? String(json.RootElement, "reason") : null);
    }
}
