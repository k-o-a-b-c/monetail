using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Monetail.Api.Authentication;
using Monetail.Api.Features.Monetail.Ledgers;

namespace Monetail.Api.Persistence;

/// <summary>API全体の保存モデルと移行を管理する。</summary>
public sealed class ApiDbContext(DbContextOptions<ApiDbContext> options)
    : IdentityDbContext<AuthUser>(options)
{
    /// <summary>API専用の移行履歴を使うSQLite接続を構成する。</summary>
    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseSqlite(connectionString, sqlite => sqlite.MigrationsHistoryTable(HistoryTable));

    /// <summary>API全体の適用済み移行を記録する表名を定める。</summary>
    public const string HistoryTable = "__MonetailMigrationsHistory";

    public DbSet<EnrollmentCode> EnrollmentCodes => Set<EnrollmentCode>();
    public DbSet<AuthSession> Sessions => Set<AuthSession>();

    /// <summary>認証の保存先と制約を設定する。</summary>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        LedgerModel.Configure(builder);
        builder.Entity<AuthUser>().Property(user => user.IsEnabled).HasDefaultValue(false);
        builder.Entity<AuthUser>().Property(user => user.DisplayName).HasMaxLength(AuthUser.DisplayNameMaxLength);
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
        });
    }
}
