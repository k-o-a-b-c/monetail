using Microsoft.EntityFrameworkCore;
using Monetail.Api.Authentication;

namespace Monetail.Api.Features.Monetail.Ledgers;

/// <summary>家計簿・参加者・作成要求の保存先と制約を定義する。</summary>
public static class LedgerModel
{
    /// <summary>家計簿のモデルをAPI全体の保存構成へ追加する。</summary>
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<Ledger>(ledger =>
        {
            ledger.ToTable("ledgers");
            ledger.HasKey(row => row.Id);
            ledger.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            ledger.Property(row => row.Name).HasColumnName("name").HasMaxLength(Ledger.NameMaxLength).IsRequired();
            ledger.Property(row => row.CreatedByAuthUserId).HasColumnName("created_by_auth_user_id").IsRequired();
            ledger.Property(row => row.CreatedAtUtc).HasColumnName("created_at_utc");
            ledger.Property(row => row.Revision).HasColumnName("revision").IsRequired().IsConcurrencyToken();
            ledger.HasOne<AuthUser>().WithMany().HasForeignKey(row => row.CreatedByAuthUserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<LedgerMember>(member =>
        {
            member.ToTable("ledger_members", table => table.HasCheckConstraint("CK_ledger_members_role", "role IN ('Viewer','Editor','Admin')"));
            member.HasKey(row => new { row.LedgerId, row.AuthUserId });
            member.Property(row => row.LedgerId).HasColumnName("ledger_id");
            member.Property(row => row.AuthUserId).HasColumnName("auth_user_id");
            member.Property(row => row.Role).HasColumnName("role").IsRequired();
            member.Property(row => row.JoinedAtUtc).HasColumnName("joined_at_utc");
            member.Property(row => row.Revision).HasColumnName("revision").IsRequired().IsConcurrencyToken();
            member.HasIndex(row => new { row.AuthUserId, row.LedgerId });
            member.HasOne<Ledger>().WithMany().HasForeignKey(row => row.LedgerId).OnDelete(DeleteBehavior.Restrict);
            member.HasOne<AuthUser>().WithMany().HasForeignKey(row => row.AuthUserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<LedgerCreationRequest>(request =>
        {
            request.ToTable("ledger_creation_requests");
            request.HasKey(row => row.RequestId);
            request.Property(row => row.RequestId).HasColumnName("request_id").ValueGeneratedNever();
            request.Property(row => row.ActorAuthUserId).HasColumnName("actor_auth_user_id").IsRequired();
            request.Property(row => row.LedgerId).HasColumnName("ledger_id").IsRequired();
            request.Property(row => row.RequestedName).HasColumnName("requested_name").HasMaxLength(Ledger.NameMaxLength).IsRequired();
            request.Property(row => row.CreatedAtUtc).HasColumnName("created_at_utc");
            request.HasIndex(row => row.LedgerId).IsUnique();
            request.HasOne<AuthUser>().WithMany().HasForeignKey(row => row.ActorAuthUserId).OnDelete(DeleteBehavior.Restrict);
            request.HasOne<Ledger>().WithMany().HasForeignKey(row => row.LedgerId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
