using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Monetail.Api.Authentication;

namespace Monetail.Api.Features.Monetail.Ledgers;

public sealed partial class LedgerManagementService
{
    public Task<object> SaveCategoryOrderAsync(string id, LedgerPayload input, CurrentSession actor, CancellationToken token) =>
        InLedgerAsync(id, actor, CategoryAdmin, async _ =>
        {
            var revision = input.Uuid("revision");
            var current = await context.Set<Ledger>().AsNoTracking().Where(row => row.Id == id)
                .Select(row => row.CategoryOrderRevision).SingleAsync(token);
            if (current != revision) throw Conflict(await CategoriesAsync(id, actor, token));
            if (input.Value("categoryIds").ValueKind != JsonValueKind.Array)
                throw LedgerPayload.Bad("科目の一覧を指定してください。", "categoryIds");
            var ids = new List<string>();
            foreach (var value in input.Value("categoryIds").EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.String || !Guid.TryParse(value.GetString(), out var key) || key == Guid.Empty)
                    throw LedgerPayload.Bad("科目の一覧を確認してください。", "categoryIds");
                ids.Add(key.ToString("N"));
            }
            var rows = await context.Set<LedgerCategory>().Where(row => row.LedgerId == id).ToArrayAsync(token);
            if (ids.Count != rows.Length || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count ||
                !ids.ToHashSet(StringComparer.Ordinal).SetEquals(rows.Select(row => row.Id)))
                throw LedgerPayload.Bad("現在の全科目を重複なく指定してください。", "categoryIds");
            await RequireMemberAsync(id, actor, CategoryAdmin, token);
            // 同順・0件でも科目順版だけを更新し、CRUDと同じ版で構成変更との競合を検知する。
            var count = await context.Set<Ledger>().Where(row => row.Id == id && row.CategoryOrderRevision == revision)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.CategoryOrderRevision, Version()), token);
            if (count != 1) throw Conflict(await CategoriesAsync(id, actor, token));
            var positions = ids.Select((key, index) => (key, index)).ToDictionary(value => value.key, value => value.index);
            foreach (var row in rows) row.DisplayOrder = positions[row.Id];
            await RequireMemberAsync(id, actor, CategoryAdmin, token);
            await context.SaveChangesAsync(token);
            return await CategoriesAsync(id, actor, token);
        }, token);
}
