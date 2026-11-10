using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Monetail.Api.Authentication;
using Monetail.Api.Features.Monetail.Ledgers;

namespace Monetail.Api.Features.Monetail.Receipts;

/// <summary>レシート4表の家計簿分離、参照保護、行内制約と読取索引を構成する。</summary>
public static class LedgerReceiptModel
{
    private const string LocalTimestampFormat = "yyyy-MM-dd'T'HH:mm:ss";
    private static string Int32(string column) => $"typeof({column}) = 'integer' AND {column} BETWEEN -2147483648 AND 2147483647";

    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<LedgerReceipt>(receipt =>
        {
            receipt.ToTable("ledger_receipts", table =>
            {
                table.HasCheckConstraint("CK_ledger_receipts_transaction_at",
                    "length(transaction_at) = 19 AND transaction_at GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]' " +
                    "AND transaction_at >= '0001-01-01T00:00:00' AND COALESCE(strftime('%Y-%m-%dT%H:%M:%S', transaction_at, '+0 seconds') = transaction_at, 0)");
                table.HasCheckConstraint("CK_ledger_receipts_tax_inclusion", "tax_inclusion_mode IN (0,1)");
                table.HasCheckConstraint("CK_ledger_receipts_tax_rounding", "tax_rounding_mode IN ('floor','half_up')");
                table.HasCheckConstraint("CK_ledger_receipts_total", Int32("total_amount"));
                table.HasCheckConstraint("CK_ledger_receipts_recurrence",
                    "(recurrence_interval IS NULL AND recurrence_unit IS NULL) OR " +
                    "(recurrence_interval IS NOT NULL AND recurrence_unit IS NOT NULL AND " +
                    "typeof(recurrence_interval) = 'integer' AND recurrence_interval >= 1 AND recurrence_unit IN ('Week','Month','Year'))");
            });
            receipt.HasKey(row => new { row.LedgerId, row.Id });
            receipt.Property(row => row.LedgerId).HasColumnName("ledger_id");
            receipt.Property(row => row.Id).HasColumnName("receipt_id").ValueGeneratedNever();
            receipt.Property(row => row.TransactionAt).HasColumnName("transaction_at").HasColumnType("TEXT")
                .HasConversion(value => value.ToString(LocalTimestampFormat, CultureInfo.InvariantCulture),
                    value => DateTime.ParseExact(value, LocalTimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None));
            receipt.Property(row => row.ShopName).HasColumnName("shop_name").HasMaxLength(100).IsRequired();
            receipt.Property(row => row.ShopLocation).HasColumnName("shop_location").HasMaxLength(100);
            receipt.Property(row => row.PaymentMethodId).HasColumnName("payment_method_id").IsRequired();
            receipt.Property(row => row.TaxInclusionMode).HasColumnName("tax_inclusion_mode");
            receipt.Property(row => row.TaxRoundingMode).HasColumnName("tax_rounding_mode").IsRequired();
            receipt.Property(row => row.RecurrenceInterval).HasColumnName("recurrence_interval");
            receipt.Property(row => row.RecurrenceUnit).HasColumnName("recurrence_unit");
            receipt.Property(row => row.TotalAmount).HasColumnName("total_amount");
            receipt.Property(row => row.Memo).HasColumnName("memo");
            receipt.Property(row => row.Revision).HasColumnName("revision").IsRequired().IsConcurrencyToken();
            receipt.Property(row => row.CreatedByAuthUserId).HasColumnName("created_by_auth_user_id").IsRequired();
            receipt.Property(row => row.CreatedAtUtc).HasColumnName("created_at_utc");
            receipt.Property(row => row.UpdatedByAuthUserId).HasColumnName("updated_by_auth_user_id").IsRequired();
            receipt.Property(row => row.UpdatedAtUtc).HasColumnName("updated_at_utc");
            receipt.HasOne<Ledger>().WithMany().HasForeignKey(row => row.LedgerId).OnDelete(DeleteBehavior.Restrict);
            receipt.HasOne<LedgerPaymentMethod>().WithMany().HasForeignKey(row => new { row.LedgerId, row.PaymentMethodId }).OnDelete(DeleteBehavior.Restrict);
            receipt.HasOne<AuthUser>().WithMany().HasForeignKey(row => row.CreatedByAuthUserId).OnDelete(DeleteBehavior.Restrict);
            receipt.HasOne<AuthUser>().WithMany().HasForeignKey(row => row.UpdatedByAuthUserId).OnDelete(DeleteBehavior.Restrict);
            receipt.HasIndex(row => new { row.LedgerId, row.TransactionAt, row.Id }).HasDatabaseName("ix_ledger_receipts_transaction");
        });
        builder.Entity<LedgerReceiptGroup>(group =>
        {
            group.ToTable("ledger_receipt_groups");
            group.HasKey(row => new { row.LedgerId, row.Id });
            group.Property(row => row.LedgerId).HasColumnName("ledger_id");
            group.Property(row => row.Id).HasColumnName("group_id").ValueGeneratedNever();
            group.Property(row => row.ReceiptId).HasColumnName("receipt_id").IsRequired();
            group.Property(row => row.CategoryId).HasColumnName("category_id").IsRequired();
            group.Property(row => row.Memo).HasColumnName("memo");
            group.HasOne<LedgerReceipt>().WithMany().HasForeignKey(row => new { row.LedgerId, row.ReceiptId }).OnDelete(DeleteBehavior.Restrict);
            group.HasOne<LedgerCategory>().WithMany().HasForeignKey(row => new { row.LedgerId, row.CategoryId }).OnDelete(DeleteBehavior.Restrict);
            group.HasIndex(row => new { row.LedgerId, row.ReceiptId, row.CategoryId }).IsUnique().HasDatabaseName("ux_ledger_receipt_groups_category");
        });
        builder.Entity<LedgerReceiptDetail>(detail =>
        {
            detail.ToTable("ledger_receipt_details", table =>
            {
                table.HasCheckConstraint("CK_ledger_receipt_details_order", "display_order >= 0");
                table.HasCheckConstraint("CK_ledger_receipt_details_price", Int32("unit_price"));
                table.HasCheckConstraint("CK_ledger_receipt_details_discount_amount", Int32("unit_discount_amount"));
                table.HasCheckConstraint("CK_ledger_receipt_details_quantity", "typeof(quantity) = 'integer' AND quantity BETWEEN 1 AND 2147483647");
                table.HasCheckConstraint("CK_ledger_receipt_details_expense", "expense_type IN ('Fixed','Variable','Excluded')");
                table.HasCheckConstraint("CK_ledger_receipt_details_occurrence", "occurrence_type IN ('Regular','Irregular','Temporary')");
            });
            detail.HasKey(row => new { row.LedgerId, row.Id });
            detail.Property(row => row.LedgerId).HasColumnName("ledger_id");
            detail.Property(row => row.Id).HasColumnName("detail_id").ValueGeneratedNever();
            detail.Property(row => row.GroupId).HasColumnName("group_id").IsRequired();
            detail.Property(row => row.DisplayOrder).HasColumnName("display_order");
            detail.Property(row => row.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
            detail.Property(row => row.UnitPrice).HasColumnName("unit_price");
            detail.Property(row => row.Quantity).HasColumnName("quantity");
            detail.Property(row => row.UnitDiscountAmount).HasColumnName("unit_discount_amount");
            detail.Property(row => row.DiscountRate).HasColumnName("discount_rate").HasColumnType("TEXT");
            detail.Property(row => row.TaxRate).HasColumnName("tax_rate").HasColumnType("TEXT");
            detail.Property(row => row.ExpenseType).HasColumnName("expense_type").IsRequired();
            detail.Property(row => row.OccurrenceType).HasColumnName("occurrence_type").IsRequired();
            detail.Property(row => row.Memo).HasColumnName("memo");
            detail.HasOne<LedgerReceiptGroup>().WithMany().HasForeignKey(row => new { row.LedgerId, row.GroupId }).OnDelete(DeleteBehavior.Restrict);
            detail.HasIndex(row => new { row.LedgerId, row.GroupId, row.DisplayOrder, row.Id }).HasDatabaseName("ix_ledger_receipt_details_order");
        });
        builder.Entity<LedgerReceiptDetailPerson>(person =>
        {
            person.ToTable("ledger_receipt_detail_people");
            person.HasKey(row => new { row.LedgerId, row.DetailId, row.PersonId });
            person.Property(row => row.LedgerId).HasColumnName("ledger_id");
            person.Property(row => row.DetailId).HasColumnName("detail_id");
            person.Property(row => row.PersonId).HasColumnName("person_id");
            person.HasOne<LedgerReceiptDetail>().WithMany().HasForeignKey(row => new { row.LedgerId, row.DetailId }).OnDelete(DeleteBehavior.Restrict);
            person.HasOne<LedgerPerson>().WithMany().HasForeignKey(row => new { row.LedgerId, row.PersonId }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
