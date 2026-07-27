using ExpenseTracker.Domain.Entities.User;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseTracker.Infrastructure.Persistence.Configurations
{
    public class UserPreferencesConfiguration : IEntityTypeConfiguration<UserPreferences>
    {
        public void Configure(EntityTypeBuilder<UserPreferences> builder)
        {
            // UserId is the key: one preferences row per user, no surrogate id.
            builder.HasKey(p => p.UserId);

            builder.Property(p => p.Theme)
                   .IsRequired()
                   .HasMaxLength(20)
                   .HasDefaultValue("system");

            builder.Property(p => p.BaseCurrency)
                   .IsRequired()
                   .HasMaxLength(3)
                   .HasDefaultValue("EUR");

            // No navigation property on either side — the FK exists so deleting a
            // user takes their preferences with them.
            builder.HasOne<User>()
                   .WithMany()
                   .HasForeignKey(p => p.UserId)
                   .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
