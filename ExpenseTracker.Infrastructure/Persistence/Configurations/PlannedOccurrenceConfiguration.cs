using ExpenseTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseTracker.Infrastructure.Persistence.Configurations
{
    public class PlannedOccurrenceConfiguration : IEntityTypeConfiguration<PlannedOccurrence>
    {
        public void Configure(EntityTypeBuilder<PlannedOccurrence> builder)
        {
            builder.Property(o => o.PeriodKey)
                   .IsRequired()
                   .HasMaxLength(16);

            builder.Property(o => o.DueDate)
                   .IsRequired();

            builder.Property(o => o.State)
                   .IsRequired();

            builder.Property(o => o.CommittedAt)
                   .IsRequired(false);

            // Deleting the template takes its queue with it: an occurrence
            // without a schedule behind it has nothing to commit against.
            builder.HasOne(o => o.PlannedTransaction)
                   .WithMany(p => p.Occurrences)
                   .HasForeignKey(o => o.PlannedTransactionId)
                   .IsRequired()
                   .OnDelete(DeleteBehavior.Cascade);

            // Provenance links. SetNull, not Cascade: deleting the expense
            // that August's rent became must not delete the record that
            // August's rent was dealt with, or the sweep would offer to post
            // it all over again.
            builder.HasOne(o => o.Expense)
                   .WithMany()
                   .HasForeignKey(o => o.ExpenseId)
                   .IsRequired(false)
                   .OnDelete(DeleteBehavior.SetNull);

            builder.HasOne(o => o.Income)
                   .WithMany()
                   .HasForeignKey(o => o.IncomeId)
                   .IsRequired(false)
                   .OnDelete(DeleteBehavior.SetNull);

            builder.HasOne(o => o.Transfer)
                   .WithMany()
                   .HasForeignKey(o => o.TransferId)
                   .IsRequired(false)
                   .OnDelete(DeleteBehavior.SetNull);

            // The whole idempotency guarantee, in one line. Generation runs on
            // every read of the planned data, so two tabs opening at once, a
            // crash mid-sweep or a redeploy all race to insert the same
            // period — and the database is what makes the second one lose.
            builder.HasIndex(o => new { o.PlannedTransactionId, o.PeriodKey })
                   .IsUnique();

            // The due queue: this user's pending occurrences, oldest first.
            builder.HasIndex(o => new { o.UserId, o.State, o.DueDate });
        }
    }
}
