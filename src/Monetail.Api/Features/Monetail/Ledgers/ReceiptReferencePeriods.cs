using Microsoft.EntityFrameworkCore;
using Monetail.Api.Features.Monetail.Receipts;

namespace Monetail.Api.Features.Monetail.Ledgers;

public sealed partial class LedgerManagementService
{
    private async Task CheckReceiptPeriodAsync(IQueryable<LedgerReceipt> receipts, DateOnly? from, DateOnly? to, CancellationToken token)
    {
        if (from is DateOnly first && await receipts.AnyAsync(row => row.TransactionAt < first.ToDateTime(TimeOnly.MinValue), token))
            throw LedgerPayload.Bad("既存のレシートが期間外になるため変更できません。", "validFrom");
        if (to is DateOnly last && await receipts.AnyAsync(row => row.TransactionAt > last.ToDateTime(new TimeOnly(23, 59, 59)), token))
            throw LedgerPayload.Bad("既存のレシートが期間外になるため変更できません。", "validTo");
    }
    private Task CheckPersonReceiptsAsync(string ledger, string person, DateOnly? from, DateOnly? to, CancellationToken token)
    {
        var paying = from receipt in context.Set<LedgerReceipt>()
                     join payment in context.Set<LedgerPaymentMethod>() on new { receipt.LedgerId, Id = receipt.PaymentMethodId } equals new { payment.LedgerId, payment.Id }
                     where receipt.LedgerId == ledger && payment.OwnerPersonId == person
                     select receipt;
        var targeted = from target in context.Set<LedgerReceiptDetailPerson>()
                       join detail in context.Set<LedgerReceiptDetail>() on new { target.LedgerId, Id = target.DetailId } equals new { detail.LedgerId, detail.Id }
                       join receiptGroup in context.Set<LedgerReceiptGroup>() on new { detail.LedgerId, Id = detail.GroupId } equals new { receiptGroup.LedgerId, receiptGroup.Id }
                       join receipt in context.Set<LedgerReceipt>() on new { receiptGroup.LedgerId, Id = receiptGroup.ReceiptId } equals new { receipt.LedgerId, receipt.Id }
                       where target.LedgerId == ledger && target.PersonId == person
                       select receipt;
        return CheckReceiptPeriodAsync(paying.Union(targeted), from, to, token);
    }
}
