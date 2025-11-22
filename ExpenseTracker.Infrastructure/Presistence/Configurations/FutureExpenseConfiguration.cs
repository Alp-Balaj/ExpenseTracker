using ExpenseTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseTracker.Infrastructure.Persistence.Configurations
{
    public class FutureExpenseConfiguration : IEntityTypeConfiguration<FutureExpense>
    {
        public void Configure(EntityTypeBuilder<FutureExpense> builder)
        {
            builder.Property(f => f.Amount)
                   .HasPrecision(18, 2)
                   .IsRequired();

            builder.Property(f => f.Title)
                   .IsRequired()
                   .HasMaxLength(100);

            builder.Property(f => f.Description)
                   .HasMaxLength(500);

            builder.Property(f => f.PredictedDate)
                   .IsRequired(false);

            builder.HasOne(f => f.Category)
                   .WithMany(c => c.FutureExpenses)
                   .HasForeignKey(f => f.CategoryId)
                   .IsRequired(false)
                   .OnDelete(DeleteBehavior.SetNull);
        }
    }
}
