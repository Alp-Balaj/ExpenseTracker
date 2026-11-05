using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Domain.Entities.User;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseTracker.Infrastructure.Persistence.Configurations
{
    // The UserId foreign key and index come from ConfigureUserOwnership, like every
    // other BaseEntity, so deleting a user takes their tokens with them.
    public class McpTokenConfiguration : IEntityTypeConfiguration<McpToken>
    {
        public void Configure(EntityTypeBuilder<McpToken> builder)
        {
            builder.HasKey(x => x.Id);

            builder.Property(x => x.Name).IsRequired().HasMaxLength(McpTokenLimits.NameMaxLength);
            builder.Property(x => x.TokenHash).IsRequired();
            builder.Property(x => x.LastFour).IsRequired().HasMaxLength(4);

            builder.HasIndex(x => x.TokenHash).IsUnique();
        }
    }
}
