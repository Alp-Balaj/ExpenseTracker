using ExpenseTracker.Domain.Entities.Pots;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseTracker.Infrastructure.Persistence.Configurations
{
    /// <summary>
    /// Cycle U W2 — the four ledger tables, configured together because they
    /// share one set of rules and reading them apart would hide that.
    ///
    /// <para>
    /// <b>Every money column is <c>HasPrecision(18, 2)</c></b>, matching the rest
    /// of the application. Two decimal places is not a display choice here: the
    /// share allocator works in integer cents precisely because the storage is
    /// exact to the cent, and a column that quietly held more would let
    /// <c>Σ shares ≡ amount</c> be true in the app and false in the database.
    /// </para>
    ///
    /// <para>
    /// <b>Every reference to a member is a membership, never a user id</b>, with
    /// <c>OnDelete(Restrict)</c>. A membership row is allowed to outlive its
    /// user — that is W1's whole anonymize story — so pointing the money at it
    /// means the money survives an account deletion too, which is the only way
    /// <c>Σ Bᵢ</c> can keep summing over the same rows it always did.
    /// </para>
    ///
    /// <para>
    /// <b>No pot row points at a user-owned row.</b> Account ids and the
    /// category id on a share are stored as bare Guids with no foreign key: see
    /// the entity comments. A real FK would put a pot's arithmetic inside the
    /// blast radius of one member's account deletion, which is exactly what
    /// <c>PotEntity</c> exists to prevent.
    /// </para>
    /// </summary>
    public class PotTopUpConfiguration : IEntityTypeConfiguration<PotTopUp>
    {
        public void Configure(EntityTypeBuilder<PotTopUp> builder)
        {
            builder.Property(t => t.Amount).IsRequired().HasPrecision(18, 2);
            builder.Property(t => t.Note).HasMaxLength(200);

            builder.HasOne(t => t.Pot)
                   .WithMany()
                   .HasForeignKey(t => t.PotId)
                   .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(t => t.Membership)
                   .WithMany()
                   .HasForeignKey(t => t.MembershipId)
                   .OnDelete(DeleteBehavior.Restrict);

            // "The pot's money, newest first" is the read the balance header and
            // the feed both start from.
            builder.HasIndex(t => new { t.PotId, t.Date });
            builder.HasIndex(t => t.MembershipId);
        }
    }

    public class PotExpenseConfiguration : IEntityTypeConfiguration<PotExpense>
    {
        public void Configure(EntityTypeBuilder<PotExpense> builder)
        {
            builder.Property(e => e.Title).IsRequired().HasMaxLength(100);
            builder.Property(e => e.Description).HasMaxLength(500);
            builder.Property(e => e.Amount).IsRequired().HasPrecision(18, 2);

            builder.HasOne(e => e.Pot)
                   .WithMany()
                   .HasForeignKey(e => e.PotId)
                   .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(e => e.CreatedByMembership)
                   .WithMany()
                   .HasForeignKey(e => e.CreatedByMembershipId)
                   .OnDelete(DeleteBehavior.Restrict);

            // Null for the ordinary case — the pot paid. Set only when a member
            // fronted it, which is the `outOfPocket` term of their position.
            builder.HasOne(e => e.PaidByMembership)
                   .WithMany()
                   .HasForeignKey(e => e.PaidByMembershipId)
                   .IsRequired(false)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(e => new { e.PotId, e.Date });
            builder.HasIndex(e => e.PaidByMembershipId);
        }
    }

    public class PotExpenseShareConfiguration : IEntityTypeConfiguration<PotExpenseShare>
    {
        public void Configure(EntityTypeBuilder<PotExpenseShare> builder)
        {
            builder.Property(s => s.Amount).IsRequired().HasPrecision(18, 2);

            builder.HasOne(s => s.Pot)
                   .WithMany()
                   .HasForeignKey(s => s.PotId)
                   .OnDelete(DeleteBehavior.Cascade);

            // The one cascade inside the ledger, and the contract's fifth
            // sentence in DDL form: deleting an expense reverses every share it
            // caused. The service deletes them explicitly in the same
            // transaction as the pot debit's reversal — this is the backstop
            // under that, not the mechanism.
            builder.HasOne(s => s.PotExpense)
                   .WithMany(e => e.Shares)
                   .HasForeignKey(s => s.PotExpenseId)
                   .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(s => s.Membership)
                   .WithMany()
                   .HasForeignKey(s => s.MembershipId)
                   .OnDelete(DeleteBehavior.Restrict);

            // One slice per person per expense. Two would be a double charge
            // that still summed correctly, which is the hardest kind of wrong to
            // notice.
            builder.HasIndex(s => new { s.PotExpenseId, s.MembershipId }).IsUnique();

            // "What did I consume" — the personal budget and category-total read.
            builder.HasIndex(s => s.MembershipId);

            // The "Shared" category's delete guard asks this directly.
            builder.HasIndex(s => s.CategoryId);
        }
    }

    public class PotWithdrawalConfiguration : IEntityTypeConfiguration<PotWithdrawal>
    {
        public void Configure(EntityTypeBuilder<PotWithdrawal> builder)
        {
            builder.Property(w => w.Amount).IsRequired().HasPrecision(18, 2);
            builder.Property(w => w.Note).HasMaxLength(200);

            builder.HasOne(w => w.Pot)
                   .WithMany()
                   .HasForeignKey(w => w.PotId)
                   .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(w => w.Membership)
                   .WithMany()
                   .HasForeignKey(w => w.MembershipId)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(w => new { w.PotId, w.Date });
            builder.HasIndex(w => w.MembershipId);
        }
    }
}
