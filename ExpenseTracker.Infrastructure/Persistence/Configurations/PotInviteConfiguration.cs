using ExpenseTracker.Domain.Entities.Pots;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseTracker.Infrastructure.Persistence.Configurations
{
    public class PotInviteConfiguration : IEntityTypeConfiguration<PotInvite>
    {
        public void Configure(EntityTypeBuilder<PotInvite> builder)
        {
            builder.Property(i => i.Code)
                   .IsRequired()
                   .HasMaxLength(64);

            // Globally unique, not per pot: the code is looked up on its own,
            // before the caller has any pot, so it has to identify one by itself.
            builder.HasIndex(i => i.Code)
                   .IsUnique();

            builder.HasOne(i => i.Pot)
                   .WithMany(p => p.Invites)
                   .HasForeignKey(i => i.PotId)
                   .OnDelete(DeleteBehavior.Cascade);

            // Restrict on both membership references: a membership row is never
            // deleted (leaving stamps LeftAt), so this never fires in practice —
            // it is here to keep it that way, since an invite whose issuer had
            // vanished could not say who let a member in.
            builder.HasOne(i => i.IssuedByMembership)
                   .WithMany()
                   .HasForeignKey(i => i.IssuedByMembershipId)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(i => i.RevokedByMembership)
                   .WithMany()
                   .HasForeignKey(i => i.RevokedByMembershipId)
                   .IsRequired(false)
                   .OnDelete(DeleteBehavior.Restrict);

            // The invite screen lists a pot's invites newest first.
            builder.HasIndex(i => new { i.PotId, i.CreatedAt });
        }
    }
}
