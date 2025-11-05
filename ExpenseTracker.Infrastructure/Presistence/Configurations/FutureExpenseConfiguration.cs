using ExpenseTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseTracker.Infrastructure.Persistence.Configurations
{
    public class FutureExpenseConfiguration : IEntityTypeConfiguration<FutureExpense>
    {
        public void Configure(EntityTypeBuilder<FutureExpense> builder)
        {
            builder.Property(fe => fe.Amount)
                   .HasPrecision(18, 2);

            builder.Property(fe => fe.Title)
                   .IsRequired()
                   .HasMaxLength(100);

            builder.Property(fe => fe.Category)
                   .HasMaxLength(50);
        }
    }
}
