using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Monetail.Windows.Client.Ledgers;

namespace Monetail.Windows.Client.Transport;

public sealed record CategoryInfo(Guid CategoryId, string Name, string IncomeExpense, string TaxCategoryCode,
    bool IsEnabled, int DisplayOrder, Guid Revision);
public sealed record CategoriesInfo(CategoryInfo[] Categories, Guid OrderRevision, string CurrentRole)
{
    /// <summary>表示用の判定。実際の参加・操作権限はAPIが最終検証する。</summary>
    public bool CanManage => CurrentRole == "Admin";
}
public sealed record CategoryCreationInfo(Guid CategoryId, CategoriesInfo State);

public sealed class CategoryRequestException(HttpStatusCode status, string? code, string? field,
    CategoryInfo? latestCategory = null, CategoriesInfo? latestCategories = null, PeoplePermissions? permissions = null)
    : Exception("科目の操作を完了できませんでした。")
{
    public HttpStatusCode StatusCode { get; } = status;
    public string? Code { get; } = code;
    public string? Field { get; } = field;
    public CategoryInfo? LatestCategory { get; } = latestCategory;
    public CategoriesInfo? LatestCategories { get; } = latestCategories;
    public PeoplePermissions? Permissions { get; } = permissions;
}

public sealed partial class MonetailApiClient
{
    public async Task<CategoriesInfo> ListCategoriesAsync(string access, Guid ledgerId, CancellationToken token = default)
    {
        using var json = await SendCategoryAsync(HttpMethod.Get, CategoryPath(ledgerId), access, null, token);
        return ReadCategories(json.RootElement);
    }
    public async Task<CategoryCreationInfo> CreateCategoryAsync(string access, Guid ledgerId, CategoryCreationDraft draft, CancellationToken token = default)
    {
        using var json = await SendCategoryAsync(HttpMethod.Post, CategoryPath(ledgerId), access,
            new { draft.RequestId, draft.Input.Name, draft.Input.IncomeExpense, draft.Input.TaxCategoryCode }, token);
        var state = ReadCategories(json.RootElement); var id = ReadUuidProperty(json.RootElement, "categoryId");
        if (!state.Categories.Any(row => row.CategoryId == id)) throw new ApiConnectionException();
        return new(id, state);
    }
    public async Task<CategoriesInfo> UpdateCategoryAsync(string access, Guid ledgerId, CategoryInfo row, CategoryChanges changes, CancellationToken token = default)
    {
        CheckCategoryRow(row);
        using var json = await SendCategoryAsync(HttpMethod.Patch, $"{CategoryPath(ledgerId)}/{row.CategoryId:N}", access, changes.Body(row), token, row);
        return ReadCategories(json.RootElement);
    }
    public async Task<CategoriesInfo> DeleteCategoryAsync(string access, Guid ledgerId, CategoryInfo row, CancellationToken token = default)
    {
        CheckCategoryRow(row);
        using var json = await SendCategoryAsync(HttpMethod.Delete, $"{CategoryPath(ledgerId)}/{row.CategoryId:N}", access, new { row.Revision }, token, row);
        return ReadCategories(json.RootElement);
    }
    public async Task<CategoriesInfo> SaveCategoryOrderAsync(string access, Guid ledgerId, Guid revision, IReadOnlyList<Guid> categoryIds,
        CancellationToken token = default)
    {
        CategoryInput.CheckId(revision, "revision"); var ids = categoryIds.ToArray();
        if (ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length)
            throw new CategoryInputException("categoryIds", "科目のIDを重複なく指定してください。");
        using var json = await SendCategoryAsync(HttpMethod.Put, $"{CategoryPath(ledgerId)}/order", access, new { revision, categoryIds = ids }, token);
        return ReadCategories(json.RootElement);
    }
    private static void CheckCategoryRow(CategoryInfo row)
    {
        CategoryInput.CheckId(row.CategoryId, "categoryId"); CategoryInput.CheckId(row.Revision, "revision");
    }
    private static string CategoryPath(Guid ledgerId)
    {
        CategoryInput.CheckId(ledgerId, "ledgerId"); return $"ledgers/{ledgerId:N}/categories";
    }
    private async Task<JsonDocument> SendCategoryAsync(HttpMethod method, string path, string access, object? input,
        CancellationToken token, CategoryInfo? row = null)
    {
        using var body = input is null ? null : JsonContent.Create(input);
        return Parse(await SendAsync(method, path, access, null, false, token, body,
            (status, bytes) => ReadCategoryFailure(status, bytes, row), includeStoreFailure: true));
    }
    private static CategoryRequestException ReadCategoryFailure(HttpStatusCode status, byte[] bytes, CategoryInfo? row)
    {
        if (status is HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.TooManyRequests)
        {
            var error = ReadPeopleFailure(status, bytes, false);
            return new(status, error.Code, error.Field, permissions: error.Permissions);
        }
        if (status == HttpStatusCode.ServiceUnavailable && bytes.Length == 0) return new(status, null, null);
        using var json = Parse(bytes); var root = json.RootElement; var code = String(root, "code"); var field = String(root, "field");
        if (status != HttpStatusCode.Conflict || code != "revision_conflict") return new(status, code, field);
        if (!root.TryGetProperty("latest", out var latest) || latest.ValueKind != JsonValueKind.Object) throw new ApiConnectionException();
        // DELETEの内部順序版CAS競合など、行操作でも全件のlatestが返る場合がある。
        if (latest.TryGetProperty("categories", out _)) return new(status, code, field, latestCategories: ReadCategories(latest));
        if (row is null) throw new ApiConnectionException();
        var value = ReadCategory(latest);
        if (value.CategoryId != row.CategoryId) throw new ApiConnectionException();
        return new(status, code, field, latestCategory: value);
    }
    private static CategoriesInfo ReadCategories(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("categories", out var rows) || rows.ValueKind != JsonValueKind.Array ||
            String(json, "currentRole") is not ("Admin" or "Editor" or "Viewer")) throw new ApiConnectionException();
        var categories = rows.EnumerateArray().Select(ReadCategory).ToArray();
        if (categories.Select(row => row.CategoryId).Distinct().Count() != categories.Length) throw new ApiConnectionException();
        // API配列順・同順位・欠番・無効科目を保持し、絞り込みや独自ソートをしない。
        return new(categories, ReadUuidProperty(json, "orderRevision"), String(json, "currentRole")!);
    }
    private static CategoryInfo ReadCategory(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object || String(json, "name") is not { Length: > 0 and <= 100 } name || string.IsNullOrWhiteSpace(name) ||
            !CategoryInput.IsIncomeExpense(String(json, "incomeExpense")) || !CategoryInput.IsTax(String(json, "taxCategoryCode")) ||
            !json.TryGetProperty("isEnabled", out var enabled) || enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            !json.TryGetProperty("displayOrder", out var order) || order.ValueKind != JsonValueKind.Number || !order.TryGetInt32(out var position) || position < 0)
            throw new ApiConnectionException();
        return new(ReadUuidProperty(json, "categoryId"), name, String(json, "incomeExpense")!, String(json, "taxCategoryCode")!,
            enabled.GetBoolean(), position, ReadUuidProperty(json, "revision"));
    }
}
