using ExpenseTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseTracker.Infrastructure.Persistence.Configurations
{
    public class ReimbursementConfiguration : IEntityTypeConfiguration<Reimbursement>
    {
        public void Configure(EntityTypeBuilder<Reimbursement> builder)
        {
            builder.Property(r => r.ExpectedAmount)
                   .IsRequired()
                   .HasPrecision(18, 2);

            // One reimbursement per expense, and deleting the expense takes it
            // (and, through the settlement cascade, its instalments) with it —
            // a receivable with no expense behind it would be unreachable.
            builder.HasOne(r => r.Expense)
                   .WithOne(e => e.Reimbursement)
                   .HasForeignKey<Reimbursement>(r => r.ExpenseId)
                   .IsRequired()
                   .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(r => r.ExpenseId)
                   .IsUnique();
        }
    }
}
