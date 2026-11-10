using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Monetail.Api.Authentication;
using Monetail.Api.Features.Monetail.Operations;
using Monetail.Api.Features.Monetail.Receipts;
using Monetail.Api.Features.Monetail.Taxes;

namespace Monetail.Api.Features.Monetail.Ledgers;

public sealed partial class LedgerManagementService
{
    private static string ReceiptId() => Guid.CreateVersion7().ToString("N");
    private static string D(string id) => Guid.ParseExact(id, "N").ToString("D");

    public Task<object> GetReceiptAsync(string ledger, string id, CurrentSession actor, CancellationToken token) =>
        InLedgerAsync(ledger, actor, _ => true, async _ => await ReceiptInfoAsync(await ReceiptGraphAsync(ledger, id, token), token), token);

    public Task<object> WriteReceiptAsync(string ledger, string? id, JsonElement json, CurrentSession actor, CancellationToken token)
    {
        var owners = new HashSet<string>(StringComparer.Ordinal);
        bool Permission(LedgerMember member) => member.Role == "Admin" || member.Role == "Editor" && owners.All(owner => owner == member.PersonId);
        return InLedgerAsync(ledger, actor, Permission, async _ =>
        {
            var input = ReceiptPayload.Decode(json, id is not null);
            var operation = id is null ? "receipt.create" : "receipt.update";
            var hash = OperationRequest.Hash(ledger, id, input.Revision, input.Canonical());
            var replay = await PreviousAsync(input.RequestId, operation, hash, actor, token);
            // 成功の再送は現在の認証・参加・書込ロールを確認し、業務処理/版照合を繰り返さない。
            if (replay is not null) return new ReceiptWriteResult(id is null ? 201 : 204, replay.ResultId);
            var old = id is null ? null : await ReceiptGraphAsync(ledger, id, token);
            if (old is not null) await ReceiptPaymentAccessAsync(ledger, old.Root.PaymentMethodId, owners, actor, token);
            await ReceiptPaymentAccessAsync(ledger, input.PaymentMethodId, owners, actor, token);
            if (old is not null && old.Root.Revision != input.Revision)
                throw await ReceiptConflictAsync(old, owners, actor, token);
            var amounts = await ValidateReceiptAsync(ledger, input, old, token);
            ValidateReceiptIds(input, old);
            var key = id ?? ReceiptId(); var changed = old is null || !ReceiptUnchanged(input, old);
            await RequireMemberAsync(ledger, actor, Permission, token);
            if (old is null)
            {
                var parsed = ReceiptValidation.Parse(input.Calculation);
                var createdAt = Now;
                context.Add(new LedgerReceipt
                {
                    LedgerId = ledger, Id = key, TransactionAt = parsed.TransactionAt, ShopName = input.Calculation.ShopName,
                    ShopLocation = input.Calculation.ShopLocation, PaymentMethodId = input.PaymentMethodId,
                    TaxInclusionMode = input.Calculation.TaxInclusionMode, TaxRoundingMode = input.Calculation.TaxRoundingMode,
                    RecurrenceInterval = (int?)input.Calculation.RecurrenceInterval, RecurrenceUnit = input.Calculation.RecurrenceUnit,
                    TotalAmount = amounts.TotalAmount, Memo = input.Memo, Revision = Version(),
                    CreatedByAuthUserId = actor.Session.AuthUserId, UpdatedByAuthUserId = actor.Session.AuthUserId,
                    CreatedAtUtc = createdAt, UpdatedAtUtc = createdAt
                });
                await context.SaveChangesAsync(token);
            }
            else if (changed)
            {
                var at = ReceiptValidation.Parse(input.Calculation).TransactionAt; var now = Now; var revision = Version();
                var count = await context.Set<LedgerReceipt>().Where(row => row.LedgerId == ledger && row.Id == key && row.Revision == input.Revision)
                    .ExecuteUpdateAsync(set => set.SetProperty(row => row.TransactionAt, at)
                        .SetProperty(row => row.ShopName, input.Calculation.ShopName).SetProperty(row => row.ShopLocation, input.Calculation.ShopLocation)
                        .SetProperty(row => row.PaymentMethodId, input.PaymentMethodId).SetProperty(row => row.TaxInclusionMode, input.Calculation.TaxInclusionMode)
                        .SetProperty(row => row.TaxRoundingMode, input.Calculation.TaxRoundingMode).SetProperty(row => row.RecurrenceInterval, (int?)input.Calculation.RecurrenceInterval)
                        .SetProperty(row => row.RecurrenceUnit, input.Calculation.RecurrenceUnit).SetProperty(row => row.Memo, input.Memo)
                        .SetProperty(row => row.TotalAmount, amounts.TotalAmount).SetProperty(row => row.Revision, revision)
                        .SetProperty(row => row.UpdatedByAuthUserId, actor.Session.AuthUserId).SetProperty(row => row.UpdatedAtUtc, now), token);
                if (count != 1) throw await ReceiptConflictAsync(await ReceiptGraphAsync(ledger, key, token), owners, actor, token);
            }
            if (changed) await SaveReceiptChildrenAsync(ledger, key, input, old, token);
            if (!changed)
            {
                // 親をUPDATEせず、版一致を条件に成功記録だけを原子的にINSERTする。
                var count = await context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO operation_requests(request_id,operation_type,actor_auth_user_id,request_hash,result_id,created_at_utc)
                    SELECT {input.RequestId},{operation},{actor.Session.AuthUserId},{hash},{key},{Now}
                    WHERE EXISTS(SELECT 1 FROM ledger_receipts WHERE ledger_id={ledger} AND receipt_id={key} AND revision={input.Revision})
                    """, token);
                if (count != 1) throw await ReceiptConflictAsync(await ReceiptGraphAsync(ledger, key, token), owners, actor, token);
            }
            else
            {
                Record(input.RequestId, operation, hash, key, actor);
                await context.SaveChangesAsync(token);
            }
            // 記録INSERTのトリガー等を含め、確定前にも最新マスタ・本人対応を照合する。
            await ReceiptPaymentAccessAsync(ledger, input.PaymentMethodId, owners, actor, token);
            await ValidateReceiptAsync(ledger, input, old, token);
            return new ReceiptWriteResult(id is null ? 201 : 204, key);
        }, token);
    }

    public Task<object> DeleteReceiptAsync(string ledger, string id, JsonElement json, CurrentSession actor, CancellationToken token)
    {
        var owners = new HashSet<string>(StringComparer.Ordinal);
        bool Permission(LedgerMember member) => member.Role == "Admin" || member.Role == "Editor" && owners.All(owner => owner == member.PersonId);
        return InLedgerAsync(ledger, actor, Permission, async _ =>
        {
            var (request, revision) = ReceiptPayload.Delete(json);
            var hash = OperationRequest.Hash(ledger, id, revision);
            if (await PreviousAsync(request, "receipt.delete", hash, actor, token) is not null) return new ReceiptWriteResult(204, id);
            var graph = await ReceiptGraphAsync(ledger, id, token);
            await ReceiptPaymentAccessAsync(ledger, graph.Root.PaymentMethodId, owners, actor, token);
            if (graph.Root.Revision != revision) throw await ReceiptConflictAsync(graph, owners, actor, token);
            var details = graph.Details.Select(row => row.Id).ToArray(); var groups = graph.Groups.Select(row => row.Id).ToArray();
            await context.Set<LedgerReceiptDetailPerson>().Where(row => row.LedgerId == ledger && details.Contains(row.DetailId)).ExecuteDeleteAsync(token);
            await context.Set<LedgerReceiptDetail>().Where(row => row.LedgerId == ledger && groups.Contains(row.GroupId)).ExecuteDeleteAsync(token);
            await context.Set<LedgerReceiptGroup>().Where(row => row.LedgerId == ledger && row.ReceiptId == id).ExecuteDeleteAsync(token);
            var count = await context.Set<LedgerReceipt>().Where(row => row.LedgerId == ledger && row.Id == id && row.Revision == revision).ExecuteDeleteAsync(token);
            if (count != 1) throw await ReceiptConflictAsync(graph, owners, actor, token);
            Record(request, "receipt.delete", hash, id, actor); await context.SaveChangesAsync(token);
            await RequireMemberAsync(ledger, actor, Permission, token);
            return new ReceiptWriteResult(204, id);
        }, token);
    }

    private async Task ReceiptPaymentAccessAsync(string ledger, string payment, HashSet<string> owners, CurrentSession actor, CancellationToken token)
    {
        var row = await context.Set<LedgerPaymentMethod>().AsNoTracking().SingleOrDefaultAsync(p => p.LedgerId == ledger && p.Id == payment, token) ?? throw Missing();
        owners.Add(row.OwnerPersonId);
        await RequireMemberAsync(ledger, actor, member => member.Role == "Admin" || member.Role == "Editor" && owners.All(owner => owner == member.PersonId), token);
    }

    private async Task<LedgerApiFault> ReceiptConflictAsync(ReceiptGraph graph, HashSet<string> owners, CurrentSession actor, CancellationToken token)
    {
        var latest = await ReceiptInfoAsync(graph, token);
        await ReceiptPaymentAccessAsync(graph.Root.LedgerId, graph.Root.PaymentMethodId, owners, actor, token);
        return Conflict(latest);
    }

    private async Task<ReceiptGraph> ReceiptGraphAsync(string ledger, string id, CancellationToken token)
    {
        var root = await context.Set<LedgerReceipt>().AsNoTracking().SingleOrDefaultAsync(row => row.LedgerId == ledger && row.Id == id, token) ?? throw Missing();
        var groups = await context.Set<LedgerReceiptGroup>().AsNoTracking().Where(row => row.LedgerId == ledger && row.ReceiptId == id).ToArrayAsync(token);
        var groupIds = groups.Select(row => row.Id).ToArray();
        var details = await context.Set<LedgerReceiptDetail>().AsNoTracking().Where(row => row.LedgerId == ledger && groupIds.Contains(row.GroupId)).ToArrayAsync(token);
        var detailIds = details.Select(row => row.Id).ToArray();
        var people = await context.Set<LedgerReceiptDetailPerson>().AsNoTracking().Where(row => row.LedgerId == ledger && detailIds.Contains(row.DetailId)).ToArrayAsync(token);
        return new(root, groups, details, people);
    }

    private async Task<object> ReceiptInfoAsync(ReceiptGraph graph, CancellationToken token)
    {
        var ledger = graph.Root.LedgerId;
        var categories = await context.Set<LedgerCategory>().AsNoTracking().Where(row => row.LedgerId == ledger).ToDictionaryAsync(row => row.Id, token);
        var people = await context.Set<LedgerPerson>().AsNoTracking().Where(row => row.LedgerId == ledger).ToDictionaryAsync(row => row.Id, token);
        var r = graph.Root;
        return new
        {
            receiptId = D(r.Id), transactionAt = r.TransactionAt.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            shopName = r.ShopName, shopLocation = r.ShopLocation, paymentMethodId = D(r.PaymentMethodId), taxInclusionMode = r.TaxInclusionMode,
            taxRoundingMode = r.TaxRoundingMode, recurrenceInterval = r.RecurrenceInterval, recurrenceUnit = r.RecurrenceUnit,
            memo = r.Memo, revision = D(r.Revision), groups = graph.Groups.OrderBy(g => categories[g.CategoryId].DisplayOrder).ThenBy(g => g.CategoryId, StringComparer.Ordinal).Select(g => new
            {
                groupId = D(g.Id), categoryId = D(g.CategoryId), memo = g.Memo,
                details = graph.Details.Where(d => d.GroupId == g.Id).OrderBy(d => d.DisplayOrder).ThenBy(d => d.Id, StringComparer.Ordinal).Select(d => new
                {
                    detailId = D(d.Id), displayOrder = d.DisplayOrder, name = d.Name, unitPrice = d.UnitPrice, quantity = d.Quantity, unitDiscountAmount = d.UnitDiscountAmount,
                    discountRate = Number(d.DiscountRate), taxRate = d.TaxRate is decimal rate ? Number(rate) : null,
                    expenseType = d.ExpenseType, occurrenceType = d.OccurrenceType, memo = d.Memo,
                    personIds = graph.People.Where(p => p.DetailId == d.Id).OrderBy(p => people[p.PersonId].DisplayOrder).ThenBy(p => p.PersonId, StringComparer.Ordinal).Select(p => D(p.PersonId)).ToArray()
                }).ToArray()
            }).ToArray()
        };
    }
    private static string Number(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
    private sealed record ReceiptGraph(LedgerReceipt Root, LedgerReceiptGroup[] Groups, LedgerReceiptDetail[] Details, LedgerReceiptDetailPerson[] People);
}
