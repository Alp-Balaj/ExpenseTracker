using ExpenseTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseTracker.Infrastructure.Persistence.Configurations
{
    public class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
    {
        public void Configure(EntityTypeBuilder<PushSubscription> builder)
        {
            builder.Property(s => s.Endpoint)
                   .IsRequired()
                   .HasMaxLength(500);

            builder.Property(s => s.P256dh)
                   .IsRequired()
                   .HasMaxLength(200);

            builder.Property(s => s.Auth)
                   .IsRequired()
                   .HasMaxLength(100);

            builder.Property(s => s.UserAgent)
                   .HasMaxLength(200);

            // Unique across the whole table, not per user. A push endpoint names
            // one browser installation, so the same endpoint owned by two accounts
            // would mean signing in on a friend's phone silently forwards your
            // budget warnings to them for ever. Subscribing re-points an existing
            // row at the new owner instead.
            builder.HasIndex(s => s.Endpoint)
                   .IsUnique();
        }
    }
}
