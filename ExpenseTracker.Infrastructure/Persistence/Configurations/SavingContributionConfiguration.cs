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

            builder.HasIndex(sc => new { sc.UserId, sc.Date });
        }
    }
}