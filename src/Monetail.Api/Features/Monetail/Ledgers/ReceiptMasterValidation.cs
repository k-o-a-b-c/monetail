using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Monetail.Api.Features.Monetail.Receipts;
using Monetail.Api.Features.Monetail.Taxes;

namespace Monetail.Api.Features.Monetail.Ledgers;

public sealed partial class LedgerManagementService
{
    private async Task<ReceiptAmounts> ValidateReceiptAsync(string ledger, ReceiptPayload input, ReceiptGraph? old, CancellationToken token)
    {
        DateTime at;
        try { at = ReceiptValidation.ParseHeader(input.Calculation); }
        catch (ReceiptValidationException error) { throw LedgerPayload.Bad(error.Message, error.Field); }
        var day = DateOnly.FromDateTime(at);
        var categories = await context.Set<LedgerCategory>().AsNoTracking().Where(row => row.LedgerId == ledger).ToDictionaryAsync(row => row.Id, token);
        var people = await context.Set<LedgerPerson>().AsNoTracking().Where(row => row.LedgerId == ledger).ToDictionaryAsync(row => row.Id, token);
        var payment = await context.Set<LedgerPaymentMethod>().AsNoTracking().SingleOrDefaultAsync(row => row.LedgerId == ledger && row.Id == input.PaymentMethodId, token) ?? throw Missing();
        if (!people.TryGetValue(payment.OwnerPersonId, out var owner)) throw Missing();
        if (!InPeriod(day, payment.ValidFrom, payment.ValidTo) || !InPeriod(day, owner.ValidFrom, owner.ValidTo))
            throw ReceiptMasterError("paymentMethodId", input, old);
        var periods = await context.Set<TaxRateHistory>().AsNoTracking().Select(row => new ReceiptTaxPeriod(row.ValidFrom, row.ValidTo, row.Rate)).ToArrayAsync(token);
        var master = new ReceiptMasterData(categories.ToDictionary(pair => Guid.ParseExact(pair.Key, "N"), pair => pair.Value.IncomeExpense), periods);
        // 金額・費用区分・税率の共通検証を使用し、DB参照の有効期間/所属と最初のfieldを統合する。
        ReceiptValidationException? common = null; ReceiptAmounts? amounts = null;
        try { amounts = ReceiptValidation.ValidateForSave(input.Calculation, master); }
        catch (ReceiptValidationException error) { common = error; }
        for (var i = 0; i < input.Groups.Length; i++)
        {
            var group = input.Groups[i]; var path = $"groups[{i}]";
            if (!categories.TryGetValue(group.CategoryId, out var category)) throw Missing();
            if (!InPeriod(day, category.ValidFrom, category.ValidTo)) throw ReceiptMasterError(path + ".categoryId", input, old);
            if (common is not null && (common.Field == path || common.Field == path + ".details")) throw CommonError(common);
            for (var j = 0; j < group.Details.Length; j++)
            {
                var field = $"{path}.details[{j}]"; var detail = group.Details[j];
                // 共通検証の同じ明細の入力/税率/費用エラーは対象人物より前に返す。
                if (common is not null && (common.Field == field || common.Field.StartsWith(field + ".", StringComparison.Ordinal))) throw CommonError(common);
                for (var k = 0; k < detail.PersonIds.Length; k++)
                {
                    if (!people.TryGetValue(detail.PersonIds[k], out var person)) throw Missing();
                    if (!InPeriod(day, person.ValidFrom, person.ValidTo)) throw ReceiptMasterError($"{field}.personIds[{k}]", input, old);
                }
            }
        }
        if (common is not null) throw CommonError(common);
        return amounts!;

        LedgerApiFault CommonError(ReceiptValidationException error) => error.Field.EndsWith(".taxRate", StringComparison.Ordinal) || error.Field.EndsWith(".expenseType", StringComparison.Ordinal)
            ? ReceiptMasterError(error.Field, input, old, error.Message) : LedgerPayload.Bad(error.Message, error.Field);
    }

    private static bool InPeriod(DateOnly day, DateOnly? from, DateOnly? to) => (from is null || from <= day) && (to is null || day <= to);

    private static LedgerApiFault ReceiptMasterError(string field, ReceiptPayload input, ReceiptGraph? old, string message = "取引日に有効なマスタを指定してください。")
    {
        var changed = false;
        if (old is not null && old.Root.TransactionAt.Date == DateTime.ParseExact(input.Calculation.TransactionAt, "yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture).Date)
        {
            if (field == "paymentMethodId") changed = old.Root.PaymentMethodId == input.PaymentMethodId;
            else
            {
                var match = Regex.Match(field, @"^groups\[(\d+)\](?:\.details\[(\d+)\])?(?:\.personIds\[(\d+)\])?(?:\.(?:categoryId|taxRate|expenseType))?$");
                if (match.Success)
                {
                    var g = input.Groups[int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)];
                    if (field.EndsWith(".categoryId", StringComparison.Ordinal)) changed = old.Groups.Any(row => row.Id == g.Id && row.CategoryId == g.CategoryId);
                    else if (match.Groups[2].Success)
                    {
                        var d = g.Details[int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)];
                        var saved = old.Details.SingleOrDefault(row => row.Id == d.Id);
                        if (saved is not null)
                        {
                            if (field.EndsWith(".taxRate", StringComparison.Ordinal))
                            {
                                try { changed = saved.TaxRate == (d.Calculation.TaxRate is null ? null : ReceiptValidation.DecimalValue(d.Calculation.TaxRate, field)); }
                                catch (ReceiptValidationException) { changed = false; }
                            }
                            else if (field.EndsWith(".expenseType", StringComparison.Ordinal)) changed = saved.ExpenseType == d.Calculation.ExpenseType && old.Groups.Any(row => row.Id == saved.GroupId && row.CategoryId == g.CategoryId);
                            else if (match.Groups[3].Success) changed = old.People.Any(row => row.DetailId == saved.Id && row.PersonId == d.PersonIds[int.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture)]);
                        }
                    }
                }
            }
        }
        return new(400, "invalid_input", message, field, reason: changed ? "master_changed" : null);
    }
}
