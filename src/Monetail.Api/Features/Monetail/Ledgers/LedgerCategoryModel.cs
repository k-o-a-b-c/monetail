using Microsoft.EntityFrameworkCore;
using Monetail.Api.Features.Monetail.Taxes;

namespace Monetail.Api.Features.Monetail.Ledgers;

/// <summary>科目の家計簿分離・一意性と、科目固有の収支区分を保護する。</summary>
public static class LedgerCategoryModel
{
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<LedgerCategory>(category =>
        {
            category.ToTable("ledger_categories", table =>
            {
                table.HasCheckConstraint("CK_ledger_categories_name", "length(name) BETWEEN 1 AND 100");
                table.HasCheckConstraint("CK_ledger_categories_enabled", "is_enabled IN (0,1)");
                table.HasCheckConstraint("CK_ledger_categories_order", "display_order >= 0");
                table.HasCheckConstraint("CK_ledger_categories_income_expense", "income_expense IN ('Income','Expense')");
            });
            category.HasKey(row => new { row.LedgerId, row.Id });
            category.Property(row => row.LedgerId).HasColumnName("ledger_id");
            category.Property(row => row.Id).HasColumnName("category_id").ValueGeneratedNever();
            category.Property(row => row.Name).HasColumnName("name").HasMaxLength(LedgerCategory.NameMaxLength).IsRequired().UseCollation("BINARY");
            category.Property(row => row.IsEnabled).HasColumnName("is_enabled");
            category.Property(row => row.DisplayOrder).HasColumnName("display_order");
            category.Property(row => row.TaxCategoryCode).HasColumnName("tax_category_code").IsRequired();
            category.Property(row => row.IncomeExpense).HasColumnName("income_expense").HasConversion<string>().IsRequired();
            category.Property(row => row.Revision).HasColumnName("revision").IsRequired().IsConcurrencyToken();
            category.HasOne<Ledger>().WithMany().HasForeignKey(row => row.LedgerId).OnDelete(DeleteBehavior.Restrict);
            category.HasOne<TaxCategory>().WithMany().HasForeignKey(row => row.TaxCategoryCode).OnDelete(DeleteBehavior.Restrict);
            category.HasIndex(row => new { row.LedgerId, row.Name }).IsUnique().HasDatabaseName("ux_ledger_categories_name");
            category.HasIndex(row => new { row.LedgerId, row.DisplayOrder, row.Id }).HasDatabaseName("ix_ledger_categories_order");
        });
    }
}
