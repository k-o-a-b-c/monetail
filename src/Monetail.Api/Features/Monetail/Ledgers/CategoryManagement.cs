using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Monetail.Api.Authentication;
using Monetail.Api.Features.Monetail.Operations;
using Monetail.Api.Features.Monetail.Taxes;

namespace Monetail.Api.Features.Monetail.Ledgers;

public sealed record CategoryInfo(string CategoryId, string Name, string IncomeExpense, string TaxCategoryCode,
    DateOnly? ValidFrom, DateOnly? ValidTo, int DisplayOrder, string Revision);
public sealed record CategoriesResponse(CategoryInfo[] Categories, string OrderRevision, string CurrentRole);

/// <summary>科目のCRUDを、共通の参加・認証再検証とSQLite書込トランザクション内で実行する。</summary>
public sealed partial class LedgerManagementService
{
    private static bool CategoryAdmin(LedgerMember member) => member.Role == "Admin";
    private static CategoryInfo Category(LedgerCategory row) => new(row.Id, row.Name, row.IncomeExpense.ToString(),
        row.TaxCategoryCode, row.ValidFrom, row.ValidTo, row.DisplayOrder, row.Revision);

    private async Task<CategoriesResponse> CategoriesAsync(string id, CurrentSession actor, CancellationToken token)
    {
        var member = await RequireMemberAsync(id, actor, _ => true, token);
        var rows = await context.Set<LedgerCategory>().AsNoTracking().Where(row => row.LedgerId == id)
            .OrderBy(row => row.DisplayOrder).ThenBy(row => row.Id).ToArrayAsync(token);
        var revision = await context.Set<Ledger>().Where(row => row.Id == id).Select(row => row.CategoryOrderRevision).SingleAsync(token);
        return new(rows.Select(Category).ToArray(), revision, member.Role);
    }

    public Task<object> GetCategoriesAsync(string id, CurrentSession actor, CancellationToken token) =>
        InLedgerAsync(id, actor, _ => true, async _ => await CategoriesAsync(id, actor, token), token);

    public Task<object> CreateCategoryAsync(string id, LedgerPayload input, CurrentSession actor, CancellationToken token) =>
        InLedgerAsync(id, actor, CategoryAdmin, async _ =>
        {
            var name = CategoryName(input.Text("name"));
            var income = CategoryIncome(input.Text("incomeExpense"));
            var tax = CategoryTax(input.Text("taxCategoryCode"));
            var from = input.Date("validFrom"); var to = input.Date("validTo");
            CategoryPeriod(from, to);
            var request = input.Uuid("requestId");
            var hash = OperationRequest.Hash(id, name, income.ToString(), tax, from?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), to?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            var previous = await PreviousAsync(request, "category.create", hash, actor, token);
            if (previous is not null)
            {
                if (!await context.Set<LedgerCategory>().AnyAsync(row => row.LedgerId == id && row.Id == previous.ResultId, token))
                    throw new LedgerApiFault(409, "created_result_deleted", "作成した科目は削除されています。");
                var replay = await CategoriesAsync(id, actor, token);
                return new LedgerCreatedBody(new { categoryId = previous.ResultId, replay.Categories, replay.OrderRevision, replay.CurrentRole }, true);
            }
            await UniqueCategoryNameAsync(id, null, name, token);
            var last = await context.Set<LedgerCategory>().Where(row => row.LedgerId == id).MaxAsync(row => (int?)row.DisplayOrder, token);
            if (last == int.MaxValue) throw LedgerPayload.Bad("表示順の上限に達しています。");
            await AdvanceCategoryOrderAsync(id, actor, token);
            var row = new LedgerCategory { LedgerId = id, Id = Version(), Name = name, IncomeExpense = income,
                TaxCategoryCode = tax, ValidFrom = from, ValidTo = to, DisplayOrder = (last ?? -1) + 1, Revision = Version() };
            context.Add(row);
            Record(request, "category.create", hash, row.Id, actor);
            await RequireMemberAsync(id, actor, CategoryAdmin, token);
            try { await context.SaveChangesAsync(token); }
            catch (DbUpdateException error) when (CategoryNameCollision(error)) { throw DuplicateCategoryName(); }
            var result = await CategoriesAsync(id, actor, token);
            return new LedgerCreatedBody(new { categoryId = row.Id, result.Categories, result.OrderRevision, result.CurrentRole }, false);
        }, token);

    public Task<object> UpdateCategoryAsync(string id, string key, LedgerPayload input, CurrentSession actor, CancellationToken token) =>
        InLedgerAsync(id, actor, CategoryAdmin, async _ =>
        {
            var row = await CategoryRowAsync(id, key, token);
            var revision = input.Uuid("revision");
            if (!input.Has("name") && !input.Has("taxCategoryCode") && !input.Has("incomeExpense") && !input.Has("validFrom") && !input.Has("validTo")) throw LedgerPayload.Bad("変更項目を指定してください。");
            if (row.Revision != revision) throw Conflict(Category(row));
            var name = input.Has("name") ? CategoryName(input.Text("name")) : row.Name;
            var tax = input.Has("taxCategoryCode") ? CategoryTax(input.Text("taxCategoryCode")) : row.TaxCategoryCode;
            var income = input.Has("incomeExpense") ? CategoryIncome(input.Text("incomeExpense")) : row.IncomeExpense;
            var from = input.Has("validFrom") ? input.Date("validFrom") : row.ValidFrom;
            var to = input.Has("validTo") ? input.Date("validTo") : row.ValidTo;
            CategoryPeriod(from, to);
            if (name != row.Name || tax != row.TaxCategoryCode || income != row.IncomeExpense || from != row.ValidFrom || to != row.ValidTo)
            {
                await UniqueCategoryNameAsync(id, key, name, token);
                await RequireMemberAsync(id, actor, CategoryAdmin, token);
                try
                {
                    var count = await context.Set<LedgerCategory>().Where(p => p.LedgerId == id && p.Id == key && p.Revision == revision)
                        .ExecuteUpdateAsync(set => set.SetProperty(p => p.Name, name).SetProperty(p => p.TaxCategoryCode, tax)
                            .SetProperty(p => p.IncomeExpense, income).SetProperty(p => p.ValidFrom, from)
                            .SetProperty(p => p.ValidTo, to).SetProperty(p => p.Revision, Version()), token);
                    if (count != 1) throw Conflict(Category(await CategoryRowAsync(id, key, token)));
                }
                catch (SqliteException error) when (CategoryNameCollision(error)) { throw DuplicateCategoryName(); }
            }
            // 実値不変時は書き込まない。旧版は共通SQLite書込トランザクション内で照合済み。
            return await CategoriesAsync(id, actor, token);
        }, token);

    public Task<object> DeleteCategoryAsync(string id, string key, LedgerPayload input, CurrentSession actor, CancellationToken token) =>
        InLedgerAsync(id, actor, CategoryAdmin, async _ =>
        {
            var row = await CategoryRowAsync(id, key, token);
            var revision = input.Uuid("revision");
            if (row.Revision != revision) throw Conflict(Category(row));
            await AdvanceCategoryOrderAsync(id, actor, token);
            await RequireMemberAsync(id, actor, CategoryAdmin, token);
            try
            {
                var count = await context.Set<LedgerCategory>().Where(p => p.LedgerId == id && p.Id == key && p.Revision == revision).ExecuteDeleteAsync(token);
                if (count != 1) throw Conflict(Category(await CategoryRowAsync(id, key, token)));
            }
            catch (SqliteException error) when (error.SqliteExtendedErrorCode == 787 || error.SqliteExtendedErrorCode == 1811 &&
                error.Message.Contains("FOREIGN KEY constraint failed", StringComparison.Ordinal))
            { throw new LedgerApiFault(409, "category_referenced", "この科目は参照されているため削除できません。"); }
            return await CategoriesAsync(id, actor, token);
        }, token);

    private async Task<LedgerCategory> CategoryRowAsync(string id, string key, CancellationToken token) =>
        await context.Set<LedgerCategory>().AsNoTracking().SingleOrDefaultAsync(row => row.LedgerId == id && row.Id == key, token) ?? throw Missing();
    private async Task AdvanceCategoryOrderAsync(string id, CurrentSession actor, CancellationToken token)
    {
        var revision = await context.Set<Ledger>().Where(row => row.Id == id).Select(row => row.CategoryOrderRevision).SingleAsync(token);
        await RequireMemberAsync(id, actor, CategoryAdmin, token);
        var count = await context.Set<Ledger>().Where(row => row.Id == id && row.CategoryOrderRevision == revision)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.CategoryOrderRevision, Version()), token);
        if (count != 1) throw Conflict(await CategoriesAsync(id, actor, token));
    }
    private static string CategoryName(string value)
    {
        var name = value.Trim();
        if (name.Length is < 1 or > LedgerCategory.NameMaxLength) throw LedgerPayload.Bad("科目名は1〜100文字で入力してください。", "name");
        return name;
    }
    private static IncomeExpenseKind CategoryIncome(string value) => value switch
    {
        "Income" => IncomeExpenseKind.Income, "Expense" => IncomeExpenseKind.Expense,
        _ => throw LedgerPayload.Bad("収支区分を選択してください。", "incomeExpense")
    };
    private static string CategoryTax(string value) => value is TaxCategory.Standard or TaxCategory.ReducedFood or TaxCategory.ReducedNewspaper or TaxCategory.Exempt
        ? value : throw LedgerPayload.Bad("税区分を選択してください。", "taxCategoryCode");
    private static void CategoryPeriod(DateOnly? from, DateOnly? to)
    {
        if (from > to) throw LedgerPayload.Bad("有効開始日は有効終了日以前の日付にしてください。", "validTo");
    }
    private static LedgerApiFault DuplicateCategoryName() => new(409, "duplicate_category_name", "この家計簿には同名の科目があります。", "name");
    private async Task UniqueCategoryNameAsync(string id, string? key, string name, CancellationToken token)
    {
        if (await context.Set<LedgerCategory>().AnyAsync(row => row.LedgerId == id && row.Id != key && row.Name == name, token)) throw DuplicateCategoryName();
    }
    private static bool CategoryNameCollision(Exception error) => (error is DbUpdateException ? error.InnerException : error) is SqliteException sqlite &&
        sqlite.SqliteExtendedErrorCode == 2067 && sqlite.Message.Contains("ledger_categories.ledger_id, ledger_categories.name", StringComparison.Ordinal);
}
