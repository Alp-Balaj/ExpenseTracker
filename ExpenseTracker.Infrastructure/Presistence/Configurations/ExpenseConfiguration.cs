using ExpenseTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseTracker.Infrastructure.Persistence.Configurations
{
    public class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
    {
        public void Configure(EntityTypeBuilder<Expense> builder)
        {
            builder.Property(e => e.Amount)
                   .HasPrecision(18, 2);

            builder.Property(e => e.Title)
                   .IsRequired()
                   .HasMaxLength(100);

            builder.HasOne(e => e.Account)
                   .WithMany(a => a.Expenses)
                   .HasForeignKey(e => e.AccountId)
                   .IsRequired()
                   .OnDelete(DeleteBehavior.Restrict);


            builder.HasOne(e => e.Category)
                   .WithMany(c => c.Expenses)
                   .HasForeignKey(e => e.CategoryId)
                   .OnDelete(DeleteBehavior.SetNull);
        }
    }
}
