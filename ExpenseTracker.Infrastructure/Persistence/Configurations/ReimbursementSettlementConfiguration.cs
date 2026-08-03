using ExpenseTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseTracker.Infrastructure.Persistence.Configurations
{
    public class ReimbursementSettlementConfiguration : IEntityTypeConfiguration<ReimbursementSettlement>
    {
        public void Configure(EntityTypeBuilder<ReimbursementSettlement> builder)
        {
            builder.Property(s => s.Amount)
                   .IsRequired()
                   .HasPrecision(18, 2);

            builder.Property(s => s.Date)
                   .IsRequired();

            builder.HasOne(s => s.Reimbursement)
                   .WithMany(r => r.Settlements)
                   .HasForeignKey(s => s.ReimbursementId)
                   .IsRequired()
                   .OnDelete(DeleteBehavior.Cascade);

            // Restrict, exactly as ExpenseConfiguration treats its account: an
            // account that money has moved through cannot be deleted out from
            // under the rows that record the movement.
            builder.HasOne(s => s.Account)
                   .WithMany()
                   .HasForeignKey(s => s.AccountId)
                   .IsRequired()
                   .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
