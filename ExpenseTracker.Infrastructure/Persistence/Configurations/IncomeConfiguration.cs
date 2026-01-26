using ExpenseTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseTracker.Infrastructure.Persistence.Configurations
{
    public class IncomeConfiguration : IEntityTypeConfiguration<Income>
    {
        public void Configure(EntityTypeBuilder<Income> builder)
        {
            builder.Property(i => i.Amount)
                   .HasPrecision(18, 2)
                   .IsRequired();

            builder.Property(i => i.Description)
                   .HasMaxLength(500);

            builder.Property(i => i.Date)
                   .IsRequired();

            builder.HasOne(i => i.Account)
                   .WithMany(a => a.Incomes)
                   .HasForeignKey(i => i.AccountId)
                   .IsRequired()
                   .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(i => i.Category)
                   .WithMany(c => c.Incomes)
                   .HasForeignKey(i => i.CategoryId)
                   .IsRequired(false)
                   .OnDelete(DeleteBehavior.SetNull);
        }
    }
}
