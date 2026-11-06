using Microsoft.EntityFrameworkCore;

namespace Monetail.Api.Features.Monetail.Taxes;

/// <summary>共通税区分と期間別税率を構成する。税率データは投入しない。</summary>
public static class TaxModel
{
    // SQLiteの日付関数のNULLと、不正日付の正規化も検出する。
    private static string DateConstraint(string column) =>
        $"typeof({column}) = 'text' AND length({column}) = 10 AND " +
        $"{column} GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]' AND " +
        $"{column} >= '0001-01-01' AND COALESCE(date({column}, '+0 days') = {column}, 0)";

    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<TaxCategory>(category =>
        {
            category.ToTable("tax_categories", table => table.HasCheckConstraint("CK_tax_categories_code",
                "code IN ('Standard','ReducedFood','ReducedNewspaper','Exempt')"));
            category.HasKey(row => row.Code);
            category.Property(row => row.Code).HasColumnName("code").ValueGeneratedNever();
            category.Property(row => row.Name).HasColumnName("name").IsRequired();
            category.HasData(new TaxCategory { Code = TaxCategory.Standard, Name = "標準" },
                new TaxCategory { Code = TaxCategory.ReducedFood, Name = "食品軽減" },
                new TaxCategory { Code = TaxCategory.ReducedNewspaper, Name = "新聞軽減" },
                new TaxCategory { Code = TaxCategory.Exempt, Name = "非課税" });
        });
        builder.Entity<TaxRateHistory>(history =>
        {
            history.ToTable("tax_rate_history", table =>
            {
                table.HasCheckConstraint("CK_tax_rate_history_from", DateConstraint("valid_from"));
                table.HasCheckConstraint("CK_tax_rate_history_to", "valid_to IS NULL OR (" + DateConstraint("valid_to") + ")");
                table.HasCheckConstraint("CK_tax_rate_history_period", "valid_to IS NULL OR valid_from <= valid_to");
                table.HasCheckConstraint("CK_tax_rate_history_rate",
                    "(tax_category_code = 'Exempt' AND rate IS NULL) OR (tax_category_code <> 'Exempt' AND rate IS NOT NULL)");
            });
            history.HasKey(row => row.Id);
            history.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            history.Property(row => row.TaxCategoryCode).HasColumnName("tax_category_code").IsRequired();
            history.Property(row => row.ValidFrom).HasColumnName("valid_from");
            history.Property(row => row.ValidTo).HasColumnName("valid_to");
            history.Property(row => row.Rate).HasColumnName("rate").HasColumnType("TEXT");
            history.Property(row => row.Revision).HasColumnName("revision").IsRequired().IsConcurrencyToken();
            history.HasOne<TaxCategory>().WithMany().HasForeignKey(row => row.TaxCategoryCode).OnDelete(DeleteBehavior.Restrict);
            history.HasIndex(row => new { row.TaxCategoryCode, row.ValidFrom, row.ValidTo })
                .HasDatabaseName("ix_tax_rate_history_category_period");
        });
    }
}
