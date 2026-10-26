using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Monetail.Api.Features.Monetail.Accounts;

namespace Monetail.Api.Authentication;

/// <summary>Identityと人物対応を既存SQLiteに保存する。</summary>
public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options)
    : IdentityDbContext<AuthUser>(options)
{
    public DbSet<AuthUserLink> UserLinks => Set<AuthUserLink>();
    public DbSet<EnrollmentCode> EnrollmentCodes => Set<EnrollmentCode>();
    public DbSet<AuthSession> Sessions => Set<AuthSession>();

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
        builder.Entity<EnrollmentCode>(code =>
        {
            code.ToTable("auth_enrollment_codes");
            code.HasKey(row => row.CodeHash);
            code.Property(row => row.CodeHash).IsRequired().ValueGeneratedNever();
            code.Property(row => row.AuthUserId).IsRequired();
            code.Property(row => row.Purpose).IsRequired();
            code.HasOne<AuthUser>().WithMany().HasForeignKey(row => row.AuthUserId)
                .OnDelete(DeleteBehavior.Restrict);
            code.HasIndex(row => row.AuthUserId).IsUnique()
                .HasFilter("\"ConsumedAtUtc\" IS NULL AND \"RevokedAtUtc\" IS NULL");
        });
        builder.Entity<AuthSession>(session =>
        {
            session.ToTable("auth_sessions");
            session.HasKey(row => row.IdHash);
            session.Property(row => row.IdHash).IsRequired().ValueGeneratedNever();
            session.Property(row => row.AuthUserId).IsRequired();
            session.HasOne<AuthUser>().WithMany().HasForeignKey(row => row.AuthUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<AccountCreationRequest>(request =>
        {
            request.ToTable("auth_account_creation_requests");
            request.HasKey(row => row.RequestId);
            request.Property(row => row.RequestId).ValueGeneratedNever();
            request.HasOne<AuthUser>().WithMany().HasForeignKey(row => row.CreatedByAuthUserId).OnDelete(DeleteBehavior.Restrict);
            request.HasOne<AuthUser>().WithMany().HasForeignKey(row => row.AuthUserId).OnDelete(DeleteBehavior.Restrict);
            request.HasIndex(row => row.AuthUserId).IsUnique();
            request.HasOne<LegacyPerson>().WithMany().HasForeignKey(row => row.UserCode).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
