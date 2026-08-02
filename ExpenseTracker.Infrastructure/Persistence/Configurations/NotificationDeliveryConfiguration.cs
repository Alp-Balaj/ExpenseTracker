using ExpenseTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseTracker.Infrastructure.Persistence.Configurations
{
    public class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
    {
        public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
        {
            builder.Property(d => d.DedupeKey)
                   .IsRequired()
                   .HasMaxLength(200);

            builder.Property(d => d.Kind)
                   .IsRequired();

            // The dedupe guarantee, in one line — the same shape and for the same
            // reason as PlannedOccurrence's (PlannedTransactionId, PeriodKey):
            // the jobs are catch-up by design and re-evaluate the same state on
            // every run, so what stops a second "Groceries is at 80%" is the
            // database refusing to store the record of one.
            builder.HasIndex(d => new { d.UserId, d.Kind, d.DedupeKey })
                   .IsUnique();
        }
    }
}
