using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Monetail.Api.Authentication;
using Monetail.Api.Features.Monetail.Ledgers;

namespace Monetail.Api.Features.Monetail.Transfers;

/// <summary>確定済みの13列・家計簿分離・参照保護・SQLite基本CHECK・一覧索引だけを構成する。</summary>
public static class LedgerTransferModel
{
    private const string LocalTimestampFormat = "yyyy-MM-dd'T'HH:mm:ss";

    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<LedgerTransfer>(transfer =>
        {
            transfer.ToTable("ledger_transfers", table =>
            {
                table.HasCheckConstraint("CK_ledger_transfers_payment_methods", "from_payment_method_id <> to_payment_method_id");
                table.HasCheckConstraint("CK_ledger_transfers_amount", "typeof(amount) = 'integer' AND amount BETWEEN 1 AND 2147483647");
                table.HasCheckConstraint("CK_ledger_transfers_name", "length(name) BETWEEN 1 AND 100 AND length(trim(name)) > 0");
                table.HasCheckConstraint("CK_ledger_transfers_memo", "memo IS NULL OR (length(memo) BETWEEN 1 AND 100 AND instr(memo, char(10)) = 0 AND instr(memo, char(13)) = 0)");
            });
            transfer.HasKey(row => new { row.LedgerId, row.Id });
            transfer.Property(row => row.LedgerId).HasColumnName("ledger_id").HasColumnType("TEXT");
            transfer.Property(row => row.Id).HasColumnName("transfer_id").HasColumnType("TEXT").ValueGeneratedNever();
            transfer.Property(row => row.TransactionAt).HasColumnName("transaction_at").HasColumnType("TEXT")
                .HasConversion(value => value.ToString(LocalTimestampFormat, CultureInfo.InvariantCulture),
                    value => DateTime.ParseExact(value, LocalTimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None));
            transfer.Property(row => row.Name).HasColumnName("name").HasColumnType("TEXT").HasMaxLength(100).IsRequired();
            transfer.Property(row => row.FromPaymentMethodId).HasColumnName("from_payment_method_id").HasColumnType("TEXT").IsRequired();
            transfer.Property(row => row.ToPaymentMethodId).HasColumnName("to_payment_method_id").HasColumnType("TEXT").IsRequired();
            transfer.Property(row => row.Amount).HasColumnName("amount").HasColumnType("INTEGER");
            transfer.Property(row => row.Memo).HasColumnName("memo").HasColumnType("TEXT").HasMaxLength(100);
            transfer.Property(row => row.Revision).HasColumnName("revision").HasColumnType("TEXT").IsRequired().IsConcurrencyToken();
            transfer.Property(row => row.CreatedByAuthUserId).HasColumnName("created_by_auth_user_id").HasColumnType("TEXT").IsRequired();
            transfer.Property(row => row.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("TEXT");
            transfer.Property(row => row.UpdatedByAuthUserId).HasColumnName("updated_by_auth_user_id").HasColumnType("TEXT").IsRequired();
            transfer.Property(row => row.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("TEXT");
            transfer.HasOne<Ledger>().WithMany().HasForeignKey(row => row.LedgerId).OnDelete(DeleteBehavior.Restrict);
            transfer.HasOne<LedgerPaymentMethod>().WithMany().HasForeignKey(row => new { row.LedgerId, row.FromPaymentMethodId }).OnDelete(DeleteBehavior.Restrict);
            transfer.HasOne<LedgerPaymentMethod>().WithMany().HasForeignKey(row => new { row.LedgerId, row.ToPaymentMethodId }).OnDelete(DeleteBehavior.Restrict);
            transfer.HasOne<AuthUser>().WithMany().HasForeignKey(row => row.CreatedByAuthUserId).OnDelete(DeleteBehavior.Restrict);
            transfer.HasOne<AuthUser>().WithMany().HasForeignKey(row => row.UpdatedByAuthUserId).OnDelete(DeleteBehavior.Restrict);
            transfer.HasIndex(row => new { row.LedgerId, row.TransactionAt, row.Id }).HasDatabaseName("ix_ledger_transfers_transaction");
        });
    }
}
