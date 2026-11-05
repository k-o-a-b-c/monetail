using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Client.Ledgers;

public sealed class PaymentMethodInputException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

public sealed record PaymentMethodInput
{
    public string Name { get; }
    public DateOnly? ValidFrom { get; }
    public DateOnly? ValidTo { get; }
    public PaymentMethodInput(string name, DateOnly? validFrom = null, DateOnly? validTo = null)
    {
        Name = NormalizeName(name); CheckPeriod(validFrom, validTo);
        ValidFrom = validFrom; ValidTo = validTo;
    }
    internal static string NormalizeName(string name)
    {
        var value = name.Trim();
        if (value.Length is < 1 or > 100) throw new PaymentMethodInputException("name", "支払方法名は1〜100文字で入力してください。");
        return value;
    }
    internal static void CheckPeriod(DateOnly? from, DateOnly? to)
    {
        if (from > to) throw new PaymentMethodInputException("validTo", "有効開始日は有効終了日以前の日付にしてください。");
    }
    internal static void CheckId(Guid id, string field)
    {
        if (id == Guid.Empty) throw new PaymentMethodInputException(field, "有効なIDを指定してください。");
    }
}

/// <summary>defaultは省略、new(null)は期間制限の解除。</summary>
public readonly record struct PaymentDateChange
{
    public bool IsSpecified { get; }
    public DateOnly? Value { get; }
    public PaymentDateChange(DateOnly? value) { IsSpecified = true; Value = value; }
}

/// <summary>名前のnullは省略。期間の省略・解除はPaymentDateChangeで区別する。</summary>
public sealed record PaymentMethodChanges
{
    public string? Name { get; }
    public PaymentDateChange ValidFrom { get; }
    public PaymentDateChange ValidTo { get; }
    public PaymentMethodChanges(string? name = null, PaymentDateChange validFrom = default, PaymentDateChange validTo = default)
    {
        if (name is null && !validFrom.IsSpecified && !validTo.IsSpecified)
            throw new PaymentMethodInputException("name", "変更項目を指定してください。");
        Name = name is null ? null : PaymentMethodInput.NormalizeName(name);
        ValidFrom = validFrom; ValidTo = validTo;
    }
    internal Dictionary<string, object?> Body(PaymentMethodInfo row)
    {
        PaymentMethodInput.CheckPeriod(ValidFrom.IsSpecified ? ValidFrom.Value : row.ValidFrom,
            ValidTo.IsSpecified ? ValidTo.Value : row.ValidTo);
        var body = new Dictionary<string, object?> { ["revision"] = row.Revision };
        if (Name is not null) body["name"] = Name;
        if (ValidFrom.IsSpecified) body["validFrom"] = ValidFrom.Value;
        if (ValidTo.IsSpecified) body["validTo"] = ValidTo.Value;
        return body;
    }
}

/// <summary>通信失敗・認証更新後も、同じ作成操作には同じ下書きを渡す。</summary>
public sealed record PaymentMethodCreationDraft
{
    public Guid RequestId { get; } = Guid.NewGuid();
    public Guid OwnerPersonId { get; }
    public PaymentMethodInput Input { get; }
    public PaymentMethodCreationDraft(Guid ownerPersonId, PaymentMethodInput input)
    {
        PaymentMethodInput.CheckId(ownerPersonId, "ownerPersonId");
        OwnerPersonId = ownerPersonId; Input = input;
    }
}

/// <summary>人物管理と同じ認証更新・別アカウント変更検知を使い、業務エラー時に自動再取得しない。</summary>
public sealed class PaymentMethodsWorkspace(MonetailApiClient api, ClientSession session, Guid ledgerId)
{
    public Task<PaymentMethodsInfo> ListAsync(CancellationToken token = default) =>
        session.ExecuteAuthenticatedAsync((access, ct) => api.ListPaymentMethodsAsync(access, ledgerId, ct), token);
    public Task<PaymentMethodCreationInfo> CreateAsync(PaymentMethodCreationDraft draft, CancellationToken token = default) =>
        session.ExecuteAuthenticatedAsync((access, ct) => api.CreatePaymentMethodAsync(access, ledgerId, draft, ct), token);
    public Task<PaymentMethodsInfo> UpdateAsync(PaymentMethodInfo row, PaymentMethodChanges changes, CancellationToken token = default) =>
        session.ExecuteAuthenticatedAsync((access, ct) => api.UpdatePaymentMethodAsync(access, ledgerId, row, changes, ct), token);
    public Task<PaymentMethodsInfo> DeleteAsync(PaymentMethodInfo row, CancellationToken token = default) =>
        session.ExecuteAuthenticatedAsync((access, ct) => api.DeletePaymentMethodAsync(access, ledgerId, row, ct), token);
    public Task<PaymentMethodsInfo> SaveOrderAsync(Guid ownerPersonId, Guid revision, IReadOnlyList<Guid> paymentMethodIds,
        CancellationToken token = default)
    {
        // 認証更新中に呼び出し元の一覧が変化しても、再送本文を変えない。
        var ids = paymentMethodIds.ToArray();
        return session.ExecuteAuthenticatedAsync((access, ct) => api.SavePaymentMethodOrderAsync(access, ledgerId, ownerPersonId, revision, ids, ct), token);
    }
}
