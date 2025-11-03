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
                   .HasPrecision(18, 2);

            builder.Property(i => i.Source)
                   .IsRequired()
                   .HasMaxLength(100);

            builder.Property(i => i.Category)
                   .HasMaxLength(50);
        }
    }
}
