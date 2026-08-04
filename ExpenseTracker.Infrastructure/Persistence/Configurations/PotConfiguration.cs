using ExpenseTracker.Domain.Entities.Pots;
using ExpenseTracker.Domain.Entities.User;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseTracker.Infrastructure.Persistence.Configurations
{
    /// <summary>
    /// Pot entities are not <c>BaseEntity</c>, so <c>ConfigureUserOwnership</c>
    /// never sees them and gives them nothing. Everything a pot table needs is
    /// therefore written here by hand — that is the cost of not being user-owned,
    /// and it is paid on purpose.
    /// </summary>
    public class PotConfiguration : IEntityTypeConfiguration<Pot>
    {
        public void Configure(EntityTypeBuilder<Pot> builder)
        {
            builder.Property(p => p.Name)
                   .IsRequired()
                   .HasMaxLength(100);

            // A code, not a Currency row: Currency is user-owned, so there is no
            // single row a group could point at.
            builder.Property(p => p.CurrencyCode)
                   .IsRequired()
                   .HasMaxLength(3);

            builder.Property(p => p.LowBalanceThreshold)
                   .IsRequired()
                   .HasPrecision(18, 2);

            // Restrict, not Cascade, and nullable: the creator deleting their
            // account must not be able to take the group's pot with it. The
            // anonymize step nulls this first; a delete that skipped it fails
            // here rather than succeeding quietly.
            builder.HasOne<User>()
                   .WithMany()
                   .HasForeignKey(p => p.CreatedByUserId)
                   .IsRequired(false)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(p => p.CreatedByUserId);
        }
    }
}
