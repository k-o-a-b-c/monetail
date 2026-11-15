using System.Globalization;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Client.Ledgers;

public sealed class TransferInputException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

/// <summary>省略/空文字と不透明カーソルを保持し、検索条件を補正しない。</summary>
public sealed record TransferListQuery
{
    public string Month { get; }
    public Guid? OwnerPersonId { get; }
    public Guid? PaymentMethodId { get; }
    public string? Q { get; }
    public string? Cursor { get; }
    public Guid? FocusTransferId { get; }
    public TransferListQuery(string month, Guid? ownerPersonId = null, Guid? paymentMethodId = null, string? q = null, string? cursor = null, Guid? focusTransferId = null)
    {
        if (month != "all" && (month is not { Length: 7 } || !DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
            throw new TransferInputException("month", "年月をYYYY-MMまたはallで指定してください。");
        if (ownerPersonId is Guid owner) TransferQueries.Id(owner, "ownerPersonId");
        if (paymentMethodId is Guid payment) TransferQueries.Id(payment, "paymentMethodId");
        if (focusTransferId is Guid focus) TransferQueries.Id(focus, "focusTransferId");
        if (focusTransferId is not null && cursor is not null) throw new TransferInputException("focusTransferId", "カーソルと同時に指定できません。");
        Month = month; OwnerPersonId = ownerPersonId; PaymentMethodId = paymentMethodId; Q = q; Cursor = cursor; FocusTransferId = focusTransferId;
    }
    internal string Query => TransferQueries.Encode(("month", Month), ("ownerPersonId", OwnerPersonId?.ToString("D")),
        ("paymentMethodId", PaymentMethodId?.ToString("D")), ("q", Q), ("cursor", Cursor), ("focusTransferId", FocusTransferId?.ToString("D")));
}

public sealed record TransferSuggestionQuery
{
    public string? Q { get; }
    public TransferSuggestionQuery(string? q = null)
    {
        TransferQueries.Text(q, "q"); Q = q;
    }
    internal string Query => TransferQueries.Encode(("q", Q));
}

public sealed record TransferAutofillQuery
{
    public string Name { get; }
    public DateOnly TransactionDate { get; }
    public Guid? FromPaymentMethodId { get; }
    public TransferAutofillQuery(string name, DateOnly transactionDate, Guid? fromPaymentMethodId = null)
    {
        if (string.IsNullOrEmpty(name)) throw new TransferInputException("name", "振替名を指定してください。");
        TransferQueries.Text(name, "name"); if (fromPaymentMethodId is Guid from) TransferQueries.Id(from, "fromPaymentMethodId");
        Name = name; TransactionDate = transactionDate; FromPaymentMethodId = fromPaymentMethodId;
    }
    internal string Query => TransferQueries.Encode(("name", Name), ("transactionDate", TransactionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
        ("fromPaymentMethodId", FromPaymentMethodId?.ToString("D")));
}

internal static class TransferQueries
{
    internal static void Id(Guid id, string field) { if (id == Guid.Empty) throw new TransferInputException(field, "有効なIDを指定してください。"); }
    internal static void Text(string? text, string field) { if (text?.Length > 100) throw new TransferInputException(field, "100文字以内で指定してください。"); }
    internal static string Encode(params (string Key, string? Value)[] fields) => string.Join("&", fields.Where(f => f.Value is not null).Select(f => f.Key + "=" + Uri.EscapeDataString(f.Value!)));
}

/// <summary>認証更新を共用する振替の操作窓口。再検索/ページ追加/キャッシュ更新/入力への適用はしない。</summary>
public sealed class TransfersWorkspace(MonetailApiClient api, ClientSession session, Guid ledgerId)
{
    public Task<TransferCreatedInfo> CreateAsync(TransferWriteRequest request, CancellationToken token = default)
    {
        request.CheckTarget(ledgerId, TransferWriteOperation.Create);
        return session.ExecuteAuthenticatedAsync((access, ct) => api.CreateTransferAsync(access, request, ct), token);
    }
    public Task UpdateAsync(TransferWriteRequest request, CancellationToken token = default) => WriteAsync(request, TransferWriteOperation.Update, token);
    public Task DeleteAsync(TransferWriteRequest request, CancellationToken token = default) => WriteAsync(request, TransferWriteOperation.Delete, token);
    private Task WriteAsync(TransferWriteRequest request, TransferWriteOperation operation, CancellationToken token)
    {
        request.CheckTarget(ledgerId, operation);
        return session.ExecuteAuthenticatedAsync(async (access, ct) =>
        {
            if (operation == TransferWriteOperation.Update) await api.UpdateTransferAsync(access, request, ct);
            else await api.DeleteTransferAsync(access, request, ct);
            return true;
        }, token);
    }
    public Task<TransferListInfo> ListAsync(TransferListQuery query, CancellationToken token = default) =>
        session.ExecuteAuthenticatedAsync((access, ct) => api.ListTransfersAsync(access, ledgerId, query, ct), token);
    public Task<TransferInfo> GetAsync(Guid transferId, CancellationToken token = default) =>
        session.ExecuteAuthenticatedAsync((access, ct) => api.GetTransferAsync(access, ledgerId, transferId, ct), token);
    public Task<TransferSuggestionsInfo> SuggestAsync(TransferSuggestionQuery query, CancellationToken token = default) =>
        session.ExecuteAuthenticatedAsync((access, ct) => api.GetTransferSuggestionsAsync(access, ledgerId, query, ct), token);
    public Task<TransferAutofillInfo> AutofillAsync(TransferAutofillQuery query, CancellationToken token = default) =>
        session.ExecuteAuthenticatedAsync((access, ct) => api.GetTransferAutofillAsync(access, ledgerId, query, ct), token);
}
