using ExpenseTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseTracker.Infrastructure.Persistence.Configurations
{
    public class PlannedTransactionConfiguration : IEntityTypeConfiguration<PlannedTransaction>
    {
        public void Configure(EntityTypeBuilder<PlannedTransaction> builder)
        {
            builder.Property(p => p.Title)
                   .IsRequired()
                   .HasMaxLength(100);

            builder.Property(p => p.Amount)
                   .IsRequired()
                   .HasPrecision(18, 2);

            builder.Property(p => p.Description)
                   .IsRequired(false)
                   .HasMaxLength(500);

            builder.Property(p => p.Kind)
                   .IsRequired();

            builder.Property(p => p.Recurrence)
                   .IsRequired();

            builder.Property(p => p.StartDate)
                   .IsRequired();

            builder.Property(p => p.EndDate)
                   .IsRequired(false);

            builder.Property(p => p.IsActive)
                   .IsRequired();

            // Load-bearing, like Expense's and Income's: a template whose
            // category vanished could not post anything, so the delete is
            // refused rather than silently breaking the schedule. Null only
            // for transfers, which have no category anywhere in the model.
            builder.HasOne(p => p.Category)
                   .WithMany(c => c.PlannedTransactions)
                   .HasForeignKey(p => p.CategoryId)
                   .IsRequired(false)
                   .OnDelete(DeleteBehavior.Restrict);

            // The accounts are only defaults for the commit dialog, so unlike
            // Expense/Income/Transfer they must not block deleting an account.
            // Losing the default costs one dropdown choice at commit time;
            // refusing the delete would be a lie about what the row holds.
            builder.HasOne(p => p.FromAccount)
                   .WithMany()
                   .HasForeignKey(p => p.FromAccountId)
                   .IsRequired(false)
                   .OnDelete(DeleteBehavior.SetNull);

            builder.HasOne(p => p.ToAccount)
                   .WithMany()
                   .HasForeignKey(p => p.ToAccountId)
                   .IsRequired(false)
                   .OnDelete(DeleteBehavior.SetNull);

            // Same downgrade-don't-block rule Transfer uses: the goal link is
            // an annotation on the schedule, not its reason for existing.
            builder.HasOne(p => p.SavingGoal)
                   .WithMany()
                   .HasForeignKey(p => p.SavingGoalId)
                   .IsRequired(false)
                   .OnDelete(DeleteBehavior.SetNull);

            // Every read is "this user's templates", and generation walks them
            // to find what has come due since last time.
            builder.HasIndex(p => new { p.UserId, p.IsActive });
        }
    }
}
