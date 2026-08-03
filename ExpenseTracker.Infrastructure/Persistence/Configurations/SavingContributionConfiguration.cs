using ExpenseTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseTracker.Infrastructure.Persistence.Configurations
{
    public class SavingContributionConfiguration : IEntityTypeConfiguration<SavingContribution>
    {
        public void Configure(EntityTypeBuilder<SavingContribution> builder)
        {
            builder.Property(sc => sc.Amount)
                   .HasPrecision(18, 2)
                   .IsRequired();

            builder.Property(sc => sc.Date)
                   .IsRequired();

            builder.Property(sc => sc.Note)
                   .HasMaxLength(500)
                   .IsRequired(false);

            builder.HasOne(sc => sc.SavingGoal)
                   .WithMany(sg => sg.SavingContributions)
                   .HasForeignKey(sc => sc.GoalId)
                   .IsRequired()
                   .OnDelete(DeleteBehavior.Cascade);

            // Restrict, not cascade: an account holding reserved money cannot
            // just vanish and take the reservation with it. Release or move the
            // contributions first — the same discipline SavingGoal.Account has.
            builder.HasOne(sc => sc.Account)
                   .WithMany(a => a.SavingContributions)
                   .HasForeignKey(sc => sc.AccountId)
                   .IsRequired(false)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(sc => new { sc.UserId, sc.Date });

            // Reserved-per-account is summed on every account read, so the
            // grouping key gets its own index.
            builder.HasIndex(sc => new { sc.UserId, sc.AccountId });
        }
    }
}