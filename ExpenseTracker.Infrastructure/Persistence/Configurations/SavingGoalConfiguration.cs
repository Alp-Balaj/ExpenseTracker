using ExpenseTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseTracker.Infrastructure.Persistence.Configurations
{
    public class SavingGoalConfiguration : IEntityTypeConfiguration<SavingGoal>
    {
        public void Configure(EntityTypeBuilder<SavingGoal> builder)
        {
            builder.Property(s => s.Title)
                   .HasMaxLength(200)
                   .IsRequired();

            builder.Property(s => s.TargetAmount)
                   .HasPrecision(18, 2)
                   .IsRequired();

            builder.Property(s => s.SavedAmount)
                   .HasPrecision(18, 2)
                   .IsRequired();

            builder.Property(s => s.Deadline)
                   .IsRequired();

            builder.Property(s => s.Description)
                   .HasMaxLength(500)
                   .IsRequired(false);

            builder.HasOne(s => s.Account)
                   .WithMany(a => a.SavingGoals)
                   .HasForeignKey(s => s.AccountId)
                   .IsRequired()
                   .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(s => s.Category)
                   .WithMany(c => c.SavingGoals)
                   .HasForeignKey(s => s.CategoryId)
                   .IsRequired()
                   .OnDelete(DeleteBehavior.Restrict);
        }
    }
}