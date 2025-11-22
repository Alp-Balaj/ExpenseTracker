using ExpenseTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseTracker.Infrastructure.Persistence.Configurations
{
    public class CurrencyConfiguration : IEntityTypeConfiguration<Currency>
    {
        public void Configure(EntityTypeBuilder<Currency> builder)
        {
            builder.HasKey(c => c.Id);

            builder.Property(c => c.Code)
                .IsRequired()
                .HasMaxLength(3);

            builder.Property(c => c.Symbol)
                .IsRequired()
                .HasMaxLength(5);

            builder.Property(c => c.ExchangeRateToBase)
                .IsRequired()
                .HasColumnType("decimal(18,6)");
        }
    }
}
