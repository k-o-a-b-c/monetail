using System.Text.Json;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Monetail.Api.Authentication;
using Monetail.Api.Features.Monetail.Operations;

namespace Monetail.Api.Features.Monetail.Ledgers;

/// <summary>作成と再送でHTTP状態を分け、本文は共通の一覧形式に保つ。</summary>
public sealed record LedgerCreatedBody(object Body, bool Replayed);

public sealed partial class LedgerManagementService
{
    public Task<object> GetPeopleAsync(string id, CurrentSession actor, CancellationToken token) =>
        InLedgerAsync(id, actor, _ => true, async _ => await PeopleAsync(id, actor, token), token);

    public Task<object> CreatePersonAsync(string id, LedgerPayload input, CurrentSession actor, CancellationToken token) =>
        InLedgerAsync(id, actor, member => member.Role == "Admin", async _ =>
        {
            var name = PersonName(input.Text("name"));
            var from = input.Date("validFrom");
            var to = input.Date("validTo");
            Period(from, to);
            var requestId = input.Uuid("requestId");
            var hash = OperationRequest.Hash(id, name, from?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), to?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            var previous = await PreviousAsync(requestId, "person.create", hash, actor, token);
            if (previous is not null)
            {
                if (!await context.Set<LedgerPerson>().AnyAsync(row => row.LedgerId == id && row.Id == previous.ResultId, token))
                    throw new LedgerApiFault(409, "created_result_deleted", "作成した人物は削除されています。");
                return new LedgerCreatedBody(await PeopleAsync(id, actor, token), true);
            }
            await UniqueNameAsync(id, null, name, token);
            var last = await context.Set<LedgerPerson>().Where(row => row.LedgerId == id).MaxAsync(row => (int?)row.DisplayOrder, token);
            var person = new LedgerPerson
            {
                LedgerId = id, Id = Version(), Name = name, ValidFrom = from, ValidTo = to,
                DisplayOrder = checked((last ?? -1) + 1), Revision = Version()
            };
            context.Add(person);
            await ChangeOrderVersionAsync(id, token);
            Record(requestId, "person.create", hash, person.Id, actor);
            await context.SaveChangesAsync(token);
            return new LedgerCreatedBody(await PeopleAsync(id, actor, token), false);
        }, token);

    public Task<object> UpdatePersonAsync(string id, string personId, LedgerPayload input, CurrentSession actor, CancellationToken token) =>
        InLedgerAsync(id, actor, member => member.Role == "Admin" ||
            member.PersonId == personId && !input.Has("validFrom") && !input.Has("validTo"), async _ =>
        {
            var person = await context.Set<LedgerPerson>().SingleOrDefaultAsync(row => row.LedgerId == id && row.Id == personId, token) ?? throw Missing();
            if (person.Revision != input.Uuid("revision")) throw Conflict(Person(person));
            var name = input.Has("name") ? PersonName(input.Text("name")) : person.Name;
            var from = input.Has("validFrom") ? input.Date("validFrom") : person.ValidFrom;
            var to = input.Has("validTo") ? input.Date("validTo") : person.ValidTo;
            Period(from, to);
            await UniqueNameAsync(id, personId, name, token);
            // 業務の参照日付は、レシート等のモデル追加時に同じ書込処理で検証する。
            if (person.Name != name || person.ValidFrom != from || person.ValidTo != to)
            {
                person.Name = name; person.ValidFrom = from; person.ValidTo = to; person.Revision = Version();
                await context.SaveChangesAsync(token);
            }
            return await PeopleAsync(id, actor, token);
        }, token);

    public Task<object> DeletePersonAsync(string id, string personId, LedgerPayload input, CurrentSession actor, CancellationToken token) =>
        InLedgerAsync(id, actor, member => member.Role == "Admin", async _ =>
        {
            var person = await context.Set<LedgerPerson>().SingleOrDefaultAsync(row => row.LedgerId == id && row.Id == personId, token) ?? throw Missing();
            if (person.Revision != input.Uuid("revision")) throw Conflict(Person(person));
            context.Remove(person);
            await ChangeOrderVersionAsync(id, token);
            try { await context.SaveChangesAsync(token); }
            catch (DbUpdateException error) when (error.InnerException is SqliteException sqlite &&
                (sqlite.SqliteExtendedErrorCode == 787 || sqlite.SqliteExtendedErrorCode == 1811 &&
                    sqlite.Message.Contains("FOREIGN KEY constraint failed", StringComparison.Ordinal)))
            {
                // RESTRICTによる拒否だけを参照エラーとし、他の保存障害は503へ渡す。
                throw new LedgerApiFault(409, "person_referenced", "この人物は参照されているため削除できません。");
            }
            return await PeopleAsync(id, actor, token);
        }, token);

    public Task<object> SaveOrderAsync(string id, LedgerPayload input, CurrentSession actor, CancellationToken token) =>
        InLedgerAsync(id, actor, member => member.Role == "Admin", async _ =>
        {
            var ledger = await context.Set<Ledger>().SingleAsync(row => row.Id == id, token);
            if (ledger.PeopleOrderRevision != input.Uuid("revision")) throw Conflict(await PeopleAsync(id, actor, token));
            if (input.Value("personIds").ValueKind != JsonValueKind.Array) throw LedgerPayload.Bad("人物の一覧を指定してください。", "personIds");
            var ids = new List<string>();
            foreach (var value in input.Value("personIds").EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.String || !Guid.TryParse(value.GetString(), out var key) || key == Guid.Empty)
                    throw LedgerPayload.Bad("人物の一覧を確認してください。", "personIds");
                ids.Add(key.ToString("N"));
            }
            var people = await context.Set<LedgerPerson>().Where(row => row.LedgerId == id).ToArrayAsync(token);
            if (ids.Count != people.Length || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count ||
                !ids.ToHashSet(StringComparer.Ordinal).SetEquals(people.Select(row => row.Id)))
                throw LedgerPayload.Bad("現在の全人物を重複なく指定してください。", "personIds");
            var positions = ids.Select((key, index) => (key, index)).ToDictionary(value => value.key, value => value.index);
            foreach (var person in people) person.DisplayOrder = positions[person.Id];
            ledger.PeopleOrderRevision = Version();
            await context.SaveChangesAsync(token);
            return await PeopleAsync(id, actor, token);
        }, token);

    private async Task ChangeOrderVersionAsync(string ledgerId, CancellationToken token) =>
        (await context.Set<Ledger>().SingleAsync(row => row.Id == ledgerId, token)).PeopleOrderRevision = Version();

    private static PersonInfo Person(LedgerPerson person) => new(person.Id, person.Name, person.ValidFrom, person.ValidTo, person.DisplayOrder, person.Revision);
    private static string PersonName(string value)
    {
        var name = value.Trim();
        if (name.Length == 0) throw LedgerPayload.Bad("人物名を入力してください。", "name");
        if (name.Length > 100) throw LedgerPayload.Bad("人物名は100文字以内で入力してください。", "name");
        return name;
    }
    private static void Period(DateOnly? from, DateOnly? to)
    {
        if (from > to) throw LedgerPayload.Bad("有効開始日は有効終了日以前の日付にしてください。", "validTo");
    }
    private async Task UniqueNameAsync(string id, string? personId, string name, CancellationToken token)
    {
        if (await context.Set<LedgerPerson>().AnyAsync(row => row.LedgerId == id && row.Id != personId && row.Name == name, token))
            throw new LedgerApiFault(409, "duplicate_person_name", "同じ名前の人物が登録されています。", "name");
    }
}
