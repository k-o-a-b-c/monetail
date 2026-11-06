using Microsoft.EntityFrameworkCore;
using Monetail.Api.Authentication;
using Monetail.Api.Features.Monetail.Operations;

namespace Monetail.Api.Features.Monetail.Ledgers;

/// <summary>家計簿・参加者・作成要求の保存先と制約を定義する。</summary>
public static class LedgerModel
{
    /// <summary>家計簿のモデルをAPI全体の保存構成へ追加する。</summary>
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<LedgerNavigation>(navigation =>
        {
            navigation.ToTable("ledger_navigation");
            navigation.HasKey(row => row.AuthUserId);
            navigation.Property(row => row.AuthUserId).HasColumnName("auth_user_id").ValueGeneratedNever();
            navigation.Property(row => row.LastOpenedLedgerId).HasColumnName("last_opened_ledger_id").IsRequired();
            navigation.HasOne<AuthUser>().WithMany().HasForeignKey(row => row.AuthUserId).OnDelete(DeleteBehavior.Restrict);
            navigation.HasOne<Ledger>().WithMany().HasForeignKey(row => row.LastOpenedLedgerId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<Ledger>(ledger =>
        {
            ledger.ToTable("ledgers");
            ledger.HasKey(row => row.Id);
            ledger.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            ledger.Property(row => row.Name).HasColumnName("name").HasMaxLength(Ledger.NameMaxLength).IsRequired();
            ledger.Property(row => row.CreatedByAuthUserId).HasColumnName("created_by_auth_user_id").IsRequired();
            ledger.Property(row => row.CreatedAtUtc).HasColumnName("created_at_utc");
            ledger.Property(row => row.Revision).HasColumnName("revision").IsRequired().IsConcurrencyToken();
            ledger.Property(row => row.PeopleOrderRevision).HasColumnName("people_order_revision").IsRequired().IsConcurrencyToken();
            ledger.Property(row => row.CategoryOrderRevision).HasColumnName("category_order_revision").IsRequired();
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
            member.Property(row => row.PersonId).HasColumnName("person_id");
            member.HasIndex(row => new { row.LedgerId, row.PersonId }).IsUnique();
            member.HasOne<LedgerPerson>().WithMany().HasForeignKey(row => new { row.LedgerId, row.PersonId })
                .OnDelete(DeleteBehavior.Restrict);
            member.HasIndex(row => new { row.AuthUserId, row.LedgerId });
            member.HasOne<Ledger>().WithMany().HasForeignKey(row => row.LedgerId).OnDelete(DeleteBehavior.Restrict);
            member.HasOne<AuthUser>().WithMany().HasForeignKey(row => row.AuthUserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<LedgerPerson>(person =>
        {
            person.ToTable("ledger_people", table => table.HasCheckConstraint("CK_ledger_people_period",
                "valid_from IS NULL OR valid_to IS NULL OR valid_from <= valid_to"));
            person.HasKey(row => new { row.LedgerId, row.Id });
            person.Property(row => row.LedgerId).HasColumnName("ledger_id");
            person.Property(row => row.Id).HasColumnName("person_id").ValueGeneratedNever();
            person.Property(row => row.Name).HasColumnName("name").IsRequired().UseCollation("BINARY");
            person.Property(row => row.ValidFrom).HasColumnName("valid_from");
            person.Property(row => row.ValidTo).HasColumnName("valid_to");
            person.Property(row => row.DisplayOrder).HasColumnName("display_order");
            person.Property(row => row.Revision).HasColumnName("revision").IsRequired().IsConcurrencyToken();
            // Separate from the person edit revision: changing a payment order never conflicts with editing the person's name.
            person.Property(row => row.PaymentOrderRevision).HasColumnName("payment_order_revision").IsRequired();
            person.HasIndex(row => new { row.LedgerId, row.Name }).IsUnique();
            person.HasIndex(row => new { row.LedgerId, row.DisplayOrder });
            person.HasOne<Ledger>().WithMany().HasForeignKey(row => row.LedgerId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<LedgerPaymentMethod>(payment =>
        {
            payment.ToTable("ledger_payment_methods", table =>
            {
                table.HasCheckConstraint("CK_ledger_payment_methods_display_order", "display_order >= 0");
                table.HasCheckConstraint("CK_ledger_payment_methods_period",
                    "valid_from IS NULL OR valid_to IS NULL OR valid_from <= valid_to");
            });
            payment.HasKey(row => new { row.LedgerId, row.Id });
            payment.Property(row => row.LedgerId).HasColumnName("ledger_id");
            payment.Property(row => row.Id).HasColumnName("payment_method_id").ValueGeneratedNever();
            payment.Property(row => row.OwnerPersonId).HasColumnName("owner_person_id").IsRequired();
            payment.Property(row => row.Name).HasColumnName("name").IsRequired().UseCollation("BINARY");
            payment.Property(row => row.DisplayOrder).HasColumnName("display_order");
            payment.Property(row => row.ValidFrom).HasColumnName("valid_from");
            payment.Property(row => row.ValidTo).HasColumnName("valid_to");
            payment.Property(row => row.Revision).HasColumnName("revision").IsRequired().IsConcurrencyToken();
            payment.HasIndex(row => new { row.LedgerId, row.OwnerPersonId, row.Name })
                .IsUnique().HasDatabaseName("ux_ledger_payment_methods_owner_name");
            payment.HasIndex(row => new { row.LedgerId, row.OwnerPersonId, row.DisplayOrder, row.Id })
                .HasDatabaseName("ix_ledger_payment_methods_owner_order");
            payment.HasOne<Ledger>().WithMany().HasForeignKey(row => row.LedgerId)
                .OnDelete(DeleteBehavior.Restrict);
            payment.HasOne<LedgerPerson>().WithMany()
                .HasForeignKey(row => new { row.LedgerId, row.OwnerPersonId })
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<OperationRequest>(request =>
        {
            request.ToTable("operation_requests");
            request.HasKey(row => row.RequestId);
            request.Property(row => row.RequestId).HasColumnName("request_id").ValueGeneratedNever();
            request.Property(row => row.ActorAuthUserId).HasColumnName("actor_auth_user_id").IsRequired();
            request.Property(row => row.OperationType).HasColumnName("operation_type").IsRequired();
            request.Property(row => row.RequestHash).HasColumnName("request_hash").IsRequired();
            request.Property(row => row.ResultId).HasColumnName("result_id").IsRequired();
            request.Property(row => row.CreatedAtUtc).HasColumnName("created_at_utc");
            request.HasOne<AuthUser>().WithMany().HasForeignKey(row => row.ActorAuthUserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<LedgerInvitation>(invite =>
        {
            invite.ToTable("ledger_invitations");
            invite.HasKey(row => row.Id);
            invite.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            invite.Property(row => row.LedgerId).HasColumnName("ledger_id");
            invite.Property(row => row.Token).HasColumnName("token").IsRequired();
            invite.Property(row => row.CreatedByAuthUserId).HasColumnName("created_by_auth_user_id");
            invite.Property(row => row.CreatedAtUtc).HasColumnName("created_at_utc");
            invite.Property(row => row.ExpiresAtUtc).HasColumnName("expires_at_utc");
            invite.Property(row => row.UsedByAuthUserId).HasColumnName("used_by_auth_user_id");
            invite.Property(row => row.UsedAtUtc).HasColumnName("used_at_utc");
            invite.Property(row => row.RevokedAtUtc).HasColumnName("revoked_at_utc");
            invite.HasIndex(row => row.Token).IsUnique();
            invite.HasIndex(row => new { row.LedgerId, row.ExpiresAtUtc });
            invite.HasOne<Ledger>().WithMany().HasForeignKey(row => row.LedgerId).OnDelete(DeleteBehavior.Restrict);
            invite.HasOne<AuthUser>().WithMany().HasForeignKey(row => row.CreatedByAuthUserId).OnDelete(DeleteBehavior.Restrict);
            invite.HasOne<AuthUser>().WithMany().HasForeignKey(row => row.UsedByAuthUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
