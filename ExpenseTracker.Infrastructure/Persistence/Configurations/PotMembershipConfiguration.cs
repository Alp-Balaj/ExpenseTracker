using ExpenseTracker.Domain.Entities.Pots;
using ExpenseTracker.Domain.Entities.User;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseTracker.Infrastructure.Persistence.Configurations
{
    public class PotMembershipConfiguration : IEntityTypeConfiguration<PotMembership>
    {
        public void Configure(EntityTypeBuilder<PotMembership> builder)
        {
            builder.Property(m => m.DisplayName)
                   .IsRequired()
                   .HasMaxLength(100);

            builder.HasOne(m => m.Pot)
                   .WithMany(p => p.Memberships)
                   .HasForeignKey(m => m.PotId)
                   .OnDelete(DeleteBehavior.Cascade);

            // The load-bearing line of the whole wave.
            //
            // Nullable + Restrict is what makes anonymize-then-delete the only
            // path out. Every other user-owned table cascades from AspNetUsers
            // (ConfigureUserOwnership), which for a pot would mean one member's
            // account deletion erasing rows the rest of the group is still
            // counting on. Here the database simply refuses: a raw
            // DeleteAsync(user) with a live membership still pointing at it
            // raises a DbUpdateException, which the global handler answers 409.
            //
            // That failure is the backstop working, not a bug. Do not "fix" it
            // by loosening the FK to SetNull or Cascade — SetNull would drop the
            // anonymize step's frozen display label and Cascade would take the
            // history. The supported path is UserController.DeleteAccount, which
            // anonymizes first.
            builder.HasOne<User>()
                   .WithMany()
                   .HasForeignKey(m => m.UserId)
                   .IsRequired(false)
                   .OnDelete(DeleteBehavior.Restrict);

            // One membership row per person per pot, ever. Rejoining clears
            // LeftAt on the existing row instead of writing a second, which is
            // what keeps redemption idempotent and W2's join-order tie-break
            // stable. Filtered, because anonymized rows all carry a null UserId
            // and must not collide with each other.
            builder.HasIndex(m => new { m.PotId, m.UserId })
                   .IsUnique()
                   .HasFilter("\"UserId\" IS NOT NULL");

            // "Which pots is this user in" is the predicate every pot read
            // starts from.
            builder.HasIndex(m => new { m.UserId, m.LeftAt });
        }
    }
}
