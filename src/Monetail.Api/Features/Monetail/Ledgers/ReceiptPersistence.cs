using Microsoft.EntityFrameworkCore;
using Monetail.Api.Features.Monetail.Receipts;

namespace Monetail.Api.Features.Monetail.Ledgers;

public sealed partial class LedgerManagementService
{
    private static void ValidateReceiptIds(ReceiptPayload input, ReceiptGraph? old)
    {
        var groupIds = new HashSet<string>(); var detailIds = new HashSet<string>();
        for (var i = 0; i < input.Groups.Length; i++)
        {
            var group = input.Groups[i]; var path = $"groups[{i}]";
            if (group.Id is not null)
            {
                if (!groupIds.Add(group.Id)) throw LedgerPayload.Bad("科目グループIDを重複なく指定してください。", path + ".groupId");
                if (old is null || !old.Groups.Any(g => g.Id == group.Id)) throw Missing();
            }
            var sameCategory = old?.Groups.SingleOrDefault(g => g.CategoryId == group.CategoryId);
            if (sameCategory is not null && sameCategory.Id != group.Id) throw LedgerPayload.Bad("既存の科目グループIDを指定してください。", path + ".groupId");
            for (var j = 0; j < group.Details.Length; j++)
            {
                var detail = group.Details[j];
                if (detail.Id is not null)
                {
                    if (!detailIds.Add(detail.Id)) throw LedgerPayload.Bad("明細IDを重複なく指定してください。", $"{path}.details[{j}].detailId");
                    if (old is null || !old.Details.Any(d => d.Id == detail.Id)) throw Missing();
                }
            }
        }
    }

    private static bool ReceiptUnchanged(ReceiptPayload input, ReceiptGraph old)
    {
        var r = old.Root; var c = input.Calculation; var parsed = ReceiptValidation.Parse(c);
        if (r.TransactionAt != parsed.TransactionAt || r.ShopName != c.ShopName || r.ShopLocation != c.ShopLocation || r.PaymentMethodId != input.PaymentMethodId ||
            r.TaxInclusionMode != c.TaxInclusionMode || r.TaxRoundingMode != c.TaxRoundingMode || r.RecurrenceInterval != c.RecurrenceInterval ||
            r.RecurrenceUnit != c.RecurrenceUnit || r.Memo != input.Memo || old.Groups.Length != input.Groups.Length || old.Details.Length != input.Groups.Sum(g => g.Details.Length)) return false;
        for (var i = 0; i < input.Groups.Length; i++)
        {
            var group = input.Groups[i]; var stored = old.Groups.SingleOrDefault(g => g.Id == group.Id);
            if (stored is null || stored.CategoryId != group.CategoryId || stored.Memo != group.Memo) return false;
            for (var j = 0; j < group.Details.Length; j++)
            {
                var d = group.Details[j]; var row = old.Details.SingleOrDefault(line => line.Id == d.Id); var p = parsed.Groups[i].Details[j];
                if (row is null || row.GroupId != group.Id || row.DisplayOrder != d.Calculation.DisplayOrder || row.Name != d.Calculation.Name ||
                    row.UnitPrice != p.UnitPrice || row.Quantity != p.Quantity || row.UnitDiscountAmount != p.UnitDiscountAmount || row.DiscountRate != p.DiscountRate ||
                    row.TaxRate != p.TaxRate || row.ExpenseType != d.Calculation.ExpenseType || row.OccurrenceType != d.Calculation.OccurrenceType || row.Memo != d.Memo ||
                    !old.People.Where(person => person.DetailId == row.Id).Select(person => person.PersonId).ToHashSet(StringComparer.Ordinal).SetEquals(d.PersonIds)) return false;
            }
        }
        return true;
    }

    private async Task SaveReceiptChildrenAsync(string ledger, string receipt, ReceiptPayload input, ReceiptGraph? old, CancellationToken token)
    {
        var parsed = ReceiptValidation.Parse(input.Calculation);
        var keep = input.Groups.SelectMany(g => g.Details).Where(d => d.Id is not null).Select(d => d.Id!).ToHashSet(StringComparer.Ordinal);
        var removed = old?.Details.Where(d => !keep.Contains(d.Id)).Select(d => d.Id).ToArray() ?? [];
        if (removed.Length > 0)
        {
            await context.Set<LedgerReceiptDetailPerson>().Where(p => p.LedgerId == ledger && removed.Contains(p.DetailId)).ExecuteDeleteAsync(token);
            await context.Set<LedgerReceiptDetail>().Where(d => d.LedgerId == ledger && removed.Contains(d.Id)).ExecuteDeleteAsync(token);
        }
        var trackedGroups = await context.Set<LedgerReceiptGroup>().Where(g => g.LedgerId == ledger && g.ReceiptId == receipt).ToDictionaryAsync(g => g.Id, token);
        var groups = new List<LedgerReceiptGroup>();
        foreach (var group in input.Groups)
        {
            var row = group.Id is null ? new LedgerReceiptGroup { LedgerId = ledger, Id = ReceiptId(), ReceiptId = receipt } : trackedGroups[group.Id!];
            if (group.Id is null) context.Add(row);
            row.CategoryId = group.CategoryId; row.Memo = group.Memo; groups.Add(row);
        }
        await context.SaveChangesAsync(token); // 新しい親を先に確保し、既存明細のグループ間移動もIDを保持する。
        var trackedDetails = await context.Set<LedgerReceiptDetail>().Where(d => d.LedgerId == ledger && keep.Contains(d.Id)).ToDictionaryAsync(d => d.Id, token);
        for (var i = 0; i < input.Groups.Length; i++)
        {
            for (var j = 0; j < input.Groups[i].Details.Length; j++)
            {
                var line = input.Groups[i].Details[j]; var p = parsed.Groups[i].Details[j];
                var row = line.Id is null ? new LedgerReceiptDetail { LedgerId = ledger, Id = ReceiptId() } : trackedDetails[line.Id!];
                if (line.Id is null) context.Add(row);
                row.GroupId = groups[i].Id; row.Name = line.Calculation.Name; row.DisplayOrder = (int)line.Calculation.DisplayOrder;
                row.UnitPrice = p.UnitPrice; row.Quantity = p.Quantity; row.UnitDiscountAmount = p.UnitDiscountAmount;
                row.DiscountRate = p.DiscountRate; row.TaxRate = p.TaxRate; row.ExpenseType = line.Calculation.ExpenseType;
                row.OccurrenceType = line.Calculation.OccurrenceType; row.Memo = line.Memo;
                var previous = old?.People.Where(person => person.DetailId == row.Id).ToArray() ?? [];
                var delete = previous.Where(person => !line.PersonIds.Contains(person.PersonId, StringComparer.Ordinal)).Select(person => person.PersonId).ToArray();
                if (delete.Length > 0)
                    await context.Set<LedgerReceiptDetailPerson>().Where(person => person.LedgerId == ledger && person.DetailId == row.Id && delete.Contains(person.PersonId)).ExecuteDeleteAsync(token);
                foreach (var person in line.PersonIds.Except(previous.Select(person => person.PersonId), StringComparer.Ordinal))
                    context.Add(new LedgerReceiptDetailPerson { LedgerId = ledger, DetailId = row.Id, PersonId = person });
            }
        }
        await context.SaveChangesAsync(token);
        var current = groups.Select(g => g.Id).ToArray();
        await context.Set<LedgerReceiptGroup>().Where(g => g.LedgerId == ledger && g.ReceiptId == receipt && !current.Contains(g.Id)).ExecuteDeleteAsync(token);
    }
}
