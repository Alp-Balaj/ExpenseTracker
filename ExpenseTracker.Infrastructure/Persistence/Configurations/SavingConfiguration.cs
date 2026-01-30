using ExpenseTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseTracker.Infrastructure.Persistence.Configurations
{
    public class SavingConfiguration : IEntityTypeConfiguration<Saving>
    {
        public void Configure(EntityTypeBuilder<Saving> builder)
        {
            builder.Property(s => s.Amount)
                   .HasPrecision(18, 2)
                   .IsRequired();

            builder.Property(s => s.Date)
                   .IsRequired();

            builder.Property(s => s.Description)
                   .HasMaxLength(500);

            builder.HasOne(s => s.Account)
                   .WithMany(a => a.Savings)
                   .HasForeignKey(s => s.AccountId)
                   .IsRequired(true)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(s => s.Category)
                   .WithMany(c => c.Savings)
                   .HasForeignKey(s => s.CategoryId)
                   .IsRequired(false)
                   .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
