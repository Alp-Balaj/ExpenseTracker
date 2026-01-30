using ExpenseTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseTracker.Infrastructure.Persistence.Configurations
{
    public class CategoryConfiguration : IEntityTypeConfiguration<Category>
    {
        public void Configure(EntityTypeBuilder<Category> builder)
        {
            builder.Property(c => c.Name)
                   .IsRequired()
                   .HasMaxLength(50);

            builder.Property(c => c.Description)
                   .HasMaxLength(200)
                   .IsRequired(false);

            builder.Property(c => c.CategoryType)
                   .IsRequired()
                   .HasConversion<int>();

            builder.Property(c => c.Color)
                   .HasMaxLength(20)
                   .IsRequired(false);

            builder.HasMany(c => c.Expenses)
                   .WithOne(e => e.Category)
                   .HasForeignKey(e => e.CategoryId)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.HasMany(c => c.Incomes)
                   .WithOne(i => i.Category)
                   .HasForeignKey(i => i.CategoryId)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.HasMany(c => c.Savings)
                   .WithOne(s => s.Category)
                   .HasForeignKey(s => s.CategoryId)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.HasMany(c => c.FutureExpenses)
                   .WithOne(f => f.Category)
                   .HasForeignKey(f => f.CategoryId)
                   .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
