using ExpenseTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseTracker.Infrastructure.Persistence.Configurations
{
    public class TransferConfiguration : IEntityTypeConfiguration<Transfer>
    {
        public void Configure(EntityTypeBuilder<Transfer> builder)
        {
            builder.Property(t => t.Amount)
                   .IsRequired()
                   .HasPrecision(18, 2);

            builder.Property(t => t.Date)
                   .IsRequired();

            builder.Property(t => t.Note)
                   .IsRequired(false)
                   .HasMaxLength(500);

            // Restrict on both sides, like Expense/Income: an account that still
            // has money moving through it cannot be deleted out from under it.
            builder.HasOne(t => t.FromAccount)
                   .WithMany(a => a.TransfersOut)
                   .HasForeignKey(t => t.FromAccountId)
                   .IsRequired()
                   .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(t => t.ToAccount)
                   .WithMany(a => a.TransfersIn)
                   .HasForeignKey(t => t.ToAccountId)
                   .IsRequired()
                   .OnDelete(DeleteBehavior.Restrict);

            // The savings link is an annotation on the transfer, not its reason
            // for existing: if the goal (and with it the contribution) goes away,
            // the transfer survives with the link nulled rather than blocking the
            // delete or vanishing with it.
            builder.HasOne(t => t.SavingGoal)
                   .WithMany()
                   .HasForeignKey(t => t.SavingGoalId)
                   .IsRequired(false)
                   .OnDelete(DeleteBehavior.SetNull);

            builder.HasOne(t => t.SavingContribution)
                   .WithMany()
                   .HasForeignKey(t => t.SavingContributionId)
                   .IsRequired(false)
                   .OnDelete(DeleteBehavior.SetNull);

            builder.HasIndex(t => new { t.UserId, t.Date });
        }
    }
}
