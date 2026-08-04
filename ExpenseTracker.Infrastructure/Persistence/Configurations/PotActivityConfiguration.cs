using ExpenseTracker.Domain.Entities.Pots;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseTracker.Infrastructure.Persistence.Configurations
{
    public class PotActivityConfiguration : IEntityTypeConfiguration<PotActivity>
    {
        public void Configure(EntityTypeBuilder<PotActivity> builder)
        {
            builder.Property(a => a.TargetLabel).HasMaxLength(200);
            builder.Property(a => a.Before).HasMaxLength(400);
            builder.Property(a => a.After).HasMaxLength(400);

            builder.HasOne(a => a.Pot)
                   .WithMany(p => p.Activity)
                   .HasForeignKey(a => a.PotId)
                   .OnDelete(DeleteBehavior.Cascade);

            // Restrict: the log's whole value is that the attribution outlives
            // the member. Membership rows are never deleted, so this is a fence
            // rather than a behaviour.
            builder.HasOne(a => a.ActorMembership)
                   .WithMany()
                   .HasForeignKey(a => a.ActorMembershipId)
                   .IsRequired(false)
                   .OnDelete(DeleteBehavior.Restrict);

            // The log is read one pot at a time, newest first, and never any
            // other way.
            builder.HasIndex(a => new { a.PotId, a.CreatedAt });
        }
    }
}
