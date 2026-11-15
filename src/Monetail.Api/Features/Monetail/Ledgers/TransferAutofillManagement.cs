using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Monetail.Api.Authentication;
using Monetail.Api.Features.Monetail.Transfers;

namespace Monetail.Api.Features.Monetail.Ledgers;

public sealed partial class LedgerManagementService
{
    public async Task<object> TransferAutofillAsync(string ledger, IQueryCollection query, CurrentSession actor, CancellationToken token)
    {
        // マスタ・候補の参照と最後の認証/参加/役割照合を、既存補完APIと同じTxで行う。
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        static bool CanWrite(LedgerMember member) => member.Role == "Admin" || member.Role == "Editor" && member.PersonId is not null;
        var initial = await RequireMemberAsync(ledger, actor, CanWrite, token);
        var filter = TransferAutofillQuery.Read(query); var validSource = true;
        if (filter.FromPaymentMethodId is not null)
        {
            var payment = await context.Set<LedgerPaymentMethod>().AsNoTracking().SingleOrDefaultAsync(row => row.LedgerId == ledger && row.Id == filter.FromPaymentMethodId, token) ?? throw Missing();
            if (initial.Role == "Editor" && payment.OwnerPersonId != initial.PersonId) throw Forbidden().WithPermissions(new(initial.Role, initial.PersonId));
            var person = await context.Set<LedgerPerson>().AsNoTracking().SingleOrDefaultAsync(row => row.LedgerId == ledger && row.Id == payment.OwnerPersonId, token) ?? throw Missing();
            validSource = InPeriod(filter.Date, payment.ValidFrom, payment.ValidTo) && InPeriod(filter.Date, person.ValidFrom, person.ValidTo);
        }
        TransferAutofillRow? selected = null;
        if (validSource)
        {
            var conditions = new List<string> { "t.ledger_id=@ledger", "t.name COLLATE BINARY=@name", "f.payment_method_id<>p.payment_method_id" };
            var parameters = new List<object> { new SqliteParameter("@ledger", ledger), new SqliteParameter("@name", filter.Name), new SqliteParameter("@date", filter.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) };
            foreach (var alias in new[] { "f", "fp", "p", "pp" })
                conditions.Add($"({alias}.valid_from IS NULL OR {alias}.valid_from<=@date) AND ({alias}.valid_to IS NULL OR {alias}.valid_to>=@date)");
            if (initial.Role == "Editor")
            {
                conditions.Add("f.owner_person_id=@person AND p.owner_person_id=@person"); parameters.Add(new SqliteParameter("@person", initial.PersonId));
            }
            if (filter.FromPaymentMethodId is not null)
            {
                conditions.Add("t.from_payment_method_id=@from"); parameters.Add(new SqliteParameter("@from", filter.FromPaymentMethodId));
            }
            // 両側が使える履歴を全件から絞った後で、同じ履歴の1組だけを取得する。
            var sql = $"""
                SELECT t.from_payment_method_id AS FromPaymentMethodId,t.to_payment_method_id AS ToPaymentMethodId
                FROM ledger_transfers t
                JOIN ledger_payment_methods f ON f.ledger_id=t.ledger_id AND f.payment_method_id=t.from_payment_method_id
                JOIN ledger_people fp ON fp.ledger_id=f.ledger_id AND fp.person_id=f.owner_person_id
                JOIN ledger_payment_methods p ON p.ledger_id=t.ledger_id AND p.payment_method_id=t.to_payment_method_id
                JOIN ledger_people pp ON pp.ledger_id=p.ledger_id AND pp.person_id=p.owner_person_id
                WHERE {string.Join(" AND ", conditions)}
                ORDER BY t.updated_at_utc DESC,t.transfer_id COLLATE BINARY DESC LIMIT 1
                """;
            selected = (await context.Database.SqlQueryRaw<TransferAutofillRow>(sql, parameters.ToArray()).ToArrayAsync(token)).FirstOrDefault();
        }
        var current = await RequireMemberAsync(ledger, actor, CanWrite, token);
        // 取得後の権限昇降格や本人対応変更では、古い候補を使わず再検索もしない。
        if (current.Role != initial.Role || current.PersonId != initial.PersonId) selected = null;
        var result = new { fromPaymentMethodId = selected is null ? null : D(selected.FromPaymentMethodId), toPaymentMethodId = selected is null ? null : D(selected.ToPaymentMethodId) };
        await transaction.CommitAsync(token);
        return result;
    }
}

internal sealed class TransferAutofillRow
{
    public required string FromPaymentMethodId { get; init; }
    public required string ToPaymentMethodId { get; init; }
}
