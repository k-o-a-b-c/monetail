using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Client.Ledgers;

public sealed class CategoryInputException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

public sealed record CategoryInput
{
    public string Name { get; }
    public string IncomeExpense { get; }
    public string TaxCategoryCode { get; }
    public DateOnly? ValidFrom { get; }
    public DateOnly? ValidTo { get; }
    public CategoryInput(string name, string incomeExpense, string taxCategoryCode, DateOnly? validFrom = null, DateOnly? validTo = null)
    {
        Name = NormalizeName(name);
        if (!IsIncomeExpense(incomeExpense)) throw new CategoryInputException("incomeExpense", "収支区分を選択してください。");
        IncomeExpense = incomeExpense; TaxCategoryCode = CheckTax(taxCategoryCode);
        CheckPeriod(validFrom, validTo); ValidFrom = validFrom; ValidTo = validTo;
    }
    internal static string NormalizeName(string name)
    {
        var value = name?.Trim();
        if (value is not { Length: > 0 and <= 100 }) throw new CategoryInputException("name", "科目名は1〜100文字で入力してください。");
        return value;
    }
    internal static bool IsIncomeExpense(string? value) => value is "Income" or "Expense";
    internal static bool IsTax(string? value) => value is "Standard" or "ReducedFood" or "ReducedNewspaper" or "Exempt";
    internal static string CheckTax(string value) => IsTax(value) ? value : throw new CategoryInputException("taxCategoryCode", "税区分を選択してください。");
    internal static void CheckPeriod(DateOnly? from, DateOnly? to)
    {
        if (from > to) throw new CategoryInputException("validTo", "有効開始日は有効終了日以前の日付にしてください。");
    }
    internal static void CheckId(Guid id, string field)
    {
        if (id == Guid.Empty) throw new CategoryInputException(field, "有効なIDを指定してください。");
    }
}

/// <summary>defaultは省略、new(null)は期間制限の解除。</summary>
public readonly record struct CategoryDateChange
{
    public bool IsSpecified { get; }
    public DateOnly? Value { get; }
    public CategoryDateChange(DateOnly? value) { IsSpecified = true; Value = value; }
}

/// <summary>文字列のnullは省略、期間の省略と明示nullはCategoryDateChangeで区別する。</summary>
public sealed record CategoryChanges
{
    public string? Name { get; }
    public string? IncomeExpense { get; }
    public string? TaxCategoryCode { get; }
    public CategoryDateChange ValidFrom { get; }
    public CategoryDateChange ValidTo { get; }
    public CategoryChanges(string? name = null, string? incomeExpense = null, string? taxCategoryCode = null,
        CategoryDateChange validFrom = default, CategoryDateChange validTo = default)
    {
        if (name is null && incomeExpense is null && taxCategoryCode is null && !validFrom.IsSpecified && !validTo.IsSpecified)
            throw new CategoryInputException("name", "変更項目を指定してください。");
        if (incomeExpense is not null && !CategoryInput.IsIncomeExpense(incomeExpense))
            throw new CategoryInputException("incomeExpense", "収支区分を選択してください。");
        Name = name is null ? null : CategoryInput.NormalizeName(name); IncomeExpense = incomeExpense;
        TaxCategoryCode = taxCategoryCode is null ? null : CategoryInput.CheckTax(taxCategoryCode);
        ValidFrom = validFrom; ValidTo = validTo;
    }
    internal Dictionary<string, object?> Body(CategoryInfo row)
    {
        CategoryInput.CheckPeriod(ValidFrom.IsSpecified ? ValidFrom.Value : row.ValidFrom, ValidTo.IsSpecified ? ValidTo.Value : row.ValidTo);
        var body = new Dictionary<string, object?> { ["revision"] = row.Revision };
        if (Name is not null) body["name"] = Name;
        if (IncomeExpense is not null) body["incomeExpense"] = IncomeExpense;
        if (TaxCategoryCode is not null) body["taxCategoryCode"] = TaxCategoryCode;
        if (ValidFrom.IsSpecified) body["validFrom"] = ValidFrom.Value;
        if (ValidTo.IsSpecified) body["validTo"] = ValidTo.Value;
        return body;
    }
}

/// <summary>同じ作成操作の通信再試行・認証更新には、この下書きを再利用する。</summary>
public sealed record CategoryCreationDraft
{
    public Guid RequestId { get; } = Guid.NewGuid();
    public CategoryInput Input { get; }
    public CategoryCreationDraft(CategoryInput input) => Input = input;
}

/// <summary>既存Sessionの認証更新を共用し、成功一覧と業務エラーを呼び出し元へ返す。</summary>
public sealed class CategoriesWorkspace(MonetailApiClient api, ClientSession session, Guid ledgerId)
{
    public Task<CategoriesInfo> ListAsync(CancellationToken token = default) =>
        session.ExecuteAuthenticatedAsync((access, ct) => api.ListCategoriesAsync(access, ledgerId, ct), token);
    public Task<CategoryCreationInfo> CreateAsync(CategoryCreationDraft draft, CancellationToken token = default) =>
        session.ExecuteAuthenticatedAsync((access, ct) => api.CreateCategoryAsync(access, ledgerId, draft, ct), token);
    public Task<CategoriesInfo> UpdateAsync(CategoryInfo row, CategoryChanges changes, CancellationToken token = default) =>
        session.ExecuteAuthenticatedAsync((access, ct) => api.UpdateCategoryAsync(access, ledgerId, row, changes, ct), token);
    public Task<CategoriesInfo> DeleteAsync(CategoryInfo row, CancellationToken token = default) =>
        session.ExecuteAuthenticatedAsync((access, ct) => api.DeleteCategoryAsync(access, ledgerId, row, ct), token);
    public Task<CategoriesInfo> SaveOrderAsync(Guid revision, IReadOnlyList<Guid> categoryIds, CancellationToken token = default)
    {
        // 認証更新中の呼び出し元の変更を、再送本文へ持ち込まない。
        var ids = categoryIds.ToArray();
        return session.ExecuteAuthenticatedAsync((access, ct) => api.SaveCategoryOrderAsync(access, ledgerId, revision, ids, ct), token);
    }
}
