using Microsoft.EntityFrameworkCore;
using Monetail.Api.Features.Monetail.Receipts;
using Monetail.Api.Features.Monetail.Transfers;

namespace Monetail.Api.Features.Monetail.Ledgers;

public sealed partial class LedgerManagementService
{
    private Task<string?> FindReceiptPeriodViolationAsync(IQueryable<LedgerReceipt> receipts, DateOnly? from, DateOnly? to, CancellationToken token) =>
        FindPeriodViolationAsync(receipts.Select(row => row.TransactionAt), from, to, token);

    private async Task<string?> FindPeriodViolationAsync(IQueryable<DateTime> dates, DateOnly? from, DateOnly? to, CancellationToken token)
    {
        if (from is DateOnly first && await dates.AnyAsync(day => day < first.ToDateTime(TimeOnly.MinValue), token))
            return "validFrom";
        if (to is DateOnly last && await dates.AnyAsync(day => day > last.ToDateTime(new TimeOnly(23, 59, 59)), token))
            return "validTo";
        return null;
    }

    private Task<string?> FindPersonPeriodViolationAsync(string ledger, string person, DateOnly? from, DateOnly? to, CancellationToken token)
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
        var transferred = from transfer in context.Set<LedgerTransfer>()
                          join source in context.Set<LedgerPaymentMethod>() on new { transfer.LedgerId, Id = transfer.FromPaymentMethodId } equals new { source.LedgerId, source.Id }
                          join destination in context.Set<LedgerPaymentMethod>() on new { transfer.LedgerId, Id = transfer.ToPaymentMethodId } equals new { destination.LedgerId, destination.Id }
                          where transfer.LedgerId == ledger && (source.OwnerPersonId == person || destination.OwnerPersonId == person)
                          select transfer.TransactionAt;
        return FindPeriodViolationAsync(paying.Select(row => row.TransactionAt)
            .Union(targeted.Select(row => row.TransactionAt)).Union(transferred), from, to, token);
    }
}
