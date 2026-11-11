using System.Globalization;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Client.Ledgers;

public sealed class ReceiptInputException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

/// <summary>省略/空文字を保持する。カーソルの生成・変更や条件の正規化は行わない。</summary>
public sealed record ReceiptListQuery
{
    public string Month { get; }
    public Guid? OwnerPersonId { get; }
    public Guid? PaymentMethodId { get; }
    public string? Q { get; }
    public string? Cursor { get; }
    public ReceiptListQuery(string month, Guid? ownerPersonId = null, Guid? paymentMethodId = null, string? q = null, string? cursor = null)
    {
        if (month != "all" && (month is not { Length: 7 } || !DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
            throw new ReceiptInputException("month", "年月をYYYY-MMまたはallで指定してください。");
        if (ownerPersonId is Guid owner) ReceiptQueries.Id(owner, "ownerPersonId");
        if (paymentMethodId is Guid payment) ReceiptQueries.Id(payment, "paymentMethodId");
        Month = month; OwnerPersonId = ownerPersonId; PaymentMethodId = paymentMethodId; Q = q; Cursor = cursor;
    }
    internal string Query => ReceiptQueries.Encode(("month", Month), ("ownerPersonId", OwnerPersonId?.ToString("D")),
        ("paymentMethodId", PaymentMethodId?.ToString("D")), ("q", Q), ("cursor", Cursor));
}

public enum ReceiptSuggestionKind { ShopName, ShopLocation, ItemName }

public sealed record ReceiptSuggestionQuery
{
    public ReceiptSuggestionKind Kind { get; }
    public string? ShopName { get; }
    public Guid? CategoryId { get; }
    public string? Q { get; }
    public ReceiptSuggestionQuery(ReceiptSuggestionKind kind, string? shopName = null, Guid? categoryId = null, string? q = null)
    {
        if (!Enum.IsDefined(kind)) throw new ReceiptInputException("kind", "候補の種類を指定してください。");
        if (kind == ReceiptSuggestionKind.ShopName && shopName is not null || kind != ReceiptSuggestionKind.ItemName && categoryId is not null)
            throw new ReceiptInputException("kind", "候補の種類に対応する条件を指定してください。");
        if (kind != ReceiptSuggestionKind.ShopName && string.IsNullOrEmpty(shopName)) throw new ReceiptInputException("shopName", "店名を指定してください。");
        if (kind == ReceiptSuggestionKind.ItemName) ReceiptQueries.Id(categoryId ?? Guid.Empty, "categoryId");
        ReceiptQueries.Text(shopName, "shopName"); ReceiptQueries.Text(q, "q"); Kind = kind; ShopName = shopName; CategoryId = categoryId; Q = q;
    }
    internal string Query => ReceiptQueries.Encode(("kind", Kind switch { ReceiptSuggestionKind.ShopName => "shopName", ReceiptSuggestionKind.ShopLocation => "shopLocation", _ => "itemName" }),
        ("shopName", ShopName), ("categoryId", CategoryId?.ToString("D")), ("q", Q));
}

public sealed record ReceiptAutofillQuery
{
    public string ShopName { get; }
    public DateOnly TransactionDate { get; }
    public ReceiptAutofillQuery(string shopName, DateOnly transactionDate)
    {
        if (shopName is null) throw new ReceiptInputException("shopName", "店名を指定してください。");
        ReceiptQueries.Text(shopName, "shopName"); ShopName = shopName; TransactionDate = transactionDate;
    }
    internal string Query => ReceiptQueries.Encode(("shopName", ShopName), ("transactionDate", TransactionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
}

internal static class ReceiptQueries
{
    internal static void Id(Guid id, string field) { if (id == Guid.Empty) throw new ReceiptInputException(field, "有効なIDを指定してください。"); }
    internal static void Text(string? text, string field) { if (text?.Length > 100) throw new ReceiptInputException(field, "100文字以内で指定してください。"); }
    internal static string Encode(params (string Key, string? Value)[] fields) => string.Join("&", fields.Where(f => f.Value is not null).Select(f => f.Key + "=" + Uri.EscapeDataString(f.Value!)));
}

/// <summary>認証更新を共用する窓口。再取得・自動業務再試行・キャッシュ更新はしない。</summary>
public sealed class ReceiptsWorkspace(MonetailApiClient api, ClientSession session, Guid ledgerId)
{
    public Task CreateAsync(ReceiptWriteRequest request, CancellationToken token = default) => WriteAsync(request, ReceiptWriteOperation.Create, token);
    public Task UpdateAsync(ReceiptWriteRequest request, CancellationToken token = default) => WriteAsync(request, ReceiptWriteOperation.Update, token);
    public Task DeleteAsync(ReceiptWriteRequest request, CancellationToken token = default) => WriteAsync(request, ReceiptWriteOperation.Delete, token);
    private Task WriteAsync(ReceiptWriteRequest request, ReceiptWriteOperation operation, CancellationToken token)
    {
        request.CheckTarget(ledgerId, operation);
        return session.ExecuteAuthenticatedAsync(async (access, ct) =>
        {
            await api.WriteReceiptAsync(access, request, ct); return true;
        }, token);
    }
    public Task<ReceiptListInfo> ListAsync(ReceiptListQuery query, CancellationToken token = default) =>
        session.ExecuteAuthenticatedAsync((access, ct) => api.ListReceiptsAsync(access, ledgerId, query, ct), token);
    public Task<ReceiptInfo> GetAsync(Guid receiptId, CancellationToken token = default) =>
        session.ExecuteAuthenticatedAsync((access, ct) => api.GetReceiptAsync(access, ledgerId, receiptId, ct), token);
    public Task<ReceiptSuggestionsInfo> SuggestAsync(ReceiptSuggestionQuery query, CancellationToken token = default) =>
        session.ExecuteAuthenticatedAsync((access, ct) => api.GetReceiptSuggestionsAsync(access, ledgerId, query, ct), token);
    public Task<ReceiptAutofillInfo> AutofillAsync(ReceiptAutofillQuery query, CancellationToken token = default) =>
        session.ExecuteAuthenticatedAsync((access, ct) => api.GetReceiptAutofillAsync(access, ledgerId, query, ct), token);
}
