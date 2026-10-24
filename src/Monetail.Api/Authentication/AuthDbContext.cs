using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Monetail.Api.Authentication;

/// <summary>Identityと人物対応を既存SQLiteに保存する。</summary>
public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options)
    : IdentityDbContext<AuthUser>(options)
{
    public DbSet<AuthUserLink> UserLinks => Set<AuthUserLink>();

    /// <summary>既存人物を移行対象から除外し、認証用の制約を設定する。</summary>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<AuthUser>().Property(user => user.IsEnabled).HasDefaultValue(false);
        builder.Entity<LegacyPerson>(person =>
        {
            person.ToTable("m_user", table => table.ExcludeFromMigrations());
            person.HasKey(row => row.Code);
            person.Property(row => row.Code).HasColumnName("code").ValueGeneratedNever();
        });
        builder.Entity<AuthUserLink>(link =>
        {
            link.ToTable("auth_user_links");
            link.HasKey(row => row.AuthUserId);
            link.Property(row => row.AuthUserId).HasColumnName("auth_user_id").IsRequired();
            link.Property(row => row.UserCode).HasColumnName("user_code").IsRequired();
            link.HasIndex(row => row.UserCode).IsUnique();
            link.HasOne<AuthUser>().WithMany().HasForeignKey(row => row.AuthUserId)
                .OnDelete(DeleteBehavior.Restrict);
            link.HasOne<LegacyPerson>().WithMany().HasForeignKey(row => row.UserCode)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
