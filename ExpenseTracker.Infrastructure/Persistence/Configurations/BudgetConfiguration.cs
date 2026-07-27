using ExpenseTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseTracker.Infrastructure.Persistence.Configurations
{
    public class BudgetConfiguration : IEntityTypeConfiguration<Budget>
    {
        public void Configure(EntityTypeBuilder<Budget> builder)
        {
            builder.Property(b => b.Amount)
                   .HasPrecision(18, 2)
                   .IsRequired();

            builder.Property(b => b.Period)
                   .IsRequired();

            builder.Property(b => b.Note)
                   .HasMaxLength(200)
                   .IsRequired(false);

            builder.HasOne(b => b.Category)
                   .WithMany(c => c.Budgets)
                   .HasForeignKey(b => b.CategoryId)
                   .IsRequired(false)
                   .OnDelete(DeleteBehavior.Cascade);

            // One budget per category per period. Postgres treats NULLs as
            // distinct here, so this index does not constrain the overall
            // budget (CategoryId null) — BudgetController enforces that case
            // in application code, where it can also answer with a 400 rather
            // than a unique-violation 500.
            builder.HasIndex(b => new { b.UserId, b.CategoryId, b.Period })
                   .IsUnique();
        }
    }
}
