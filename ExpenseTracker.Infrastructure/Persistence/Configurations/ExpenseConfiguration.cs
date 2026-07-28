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
                   .IsRequired()
                   .HasPrecision(18, 2);

            builder.Property(e => e.Title)
                   .IsRequired()
                   .HasMaxLength(100);

            builder.Property(e => e.Date)
                   .IsRequired();

            builder.Property(e => e.Description)
                   .IsRequired(false)
                   .HasMaxLength(500);

            builder.HasOne(e => e.Account)
                   .WithMany(a => a.Expenses)
                   .HasForeignKey(e => e.AccountId)
                   .IsRequired()
                   .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(e => e.Category)
                   .WithMany(c => c.Expenses)
                   .HasForeignKey(e => e.CategoryId)
                   .OnDelete(DeleteBehavior.Restrict);

            // Every read is "this user's expenses", usually over a period and
            // newest-first: leading with UserId serves the filter, Date serves
            // both the range scan and the sort a page is taken from.
            builder.HasIndex(e => new { e.UserId, e.Date });
        }
    }
}