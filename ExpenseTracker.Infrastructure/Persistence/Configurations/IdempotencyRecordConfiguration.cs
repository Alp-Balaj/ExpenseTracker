using ExpenseTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseTracker.Infrastructure.Persistence.Configurations
{
    public class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
    {
        public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
        {
            builder.Property(r => r.Key)
                   .IsRequired()
                   .HasMaxLength(100);

            builder.Property(r => r.Endpoint)
                   .IsRequired()
                   .HasMaxLength(200);

            // Response bodies here are a created expense — small. Capped anyway so a
            // future endpoint returning something large cannot quietly turn this
            // table into a blob store.
            builder.Property(r => r.ResponseBody)
                   .HasMaxLength(4000);

            // The exactly-once guarantee, in one line — the same shape as
            // PlannedOccurrence's (PlannedTransactionId, PeriodKey) and
            // NotificationDelivery's dedupe key. The winner of the race is decided
            // by the database, because nothing in application code can decide it:
            // two replays can arrive on two connections at the same instant.
            //
            // Scoped per user as well as per key so one account's key can never
            // collide with, or read back, another's response.
            builder.HasIndex(r => new { r.UserId, r.Key })
                   .IsUnique();
        }
    }
}
