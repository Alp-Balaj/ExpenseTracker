using ExpenseTracker.Domain.Entities.User;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseTracker.Infrastructure.Persistence.Configurations
{
    public class OAuthClientConfiguration : IEntityTypeConfiguration<OAuthClient>
    {
        public void Configure(EntityTypeBuilder<OAuthClient> builder)
        {
            builder.HasKey(x => x.ClientId);
            builder.Property(x => x.ClientId).HasMaxLength(64);
            builder.Property(x => x.ClientName).IsRequired().HasMaxLength(100);
            // Npgsql maps List<string> to text[] natively.
            builder.Property(x => x.RedirectUris).IsRequired();
        }
    }

    // UserId foreign key and index come from ConfigureUserOwnership.
    public class OAuthAuthorizationCodeConfiguration : IEntityTypeConfiguration<OAuthAuthorizationCode>
    {
        public void Configure(EntityTypeBuilder<OAuthAuthorizationCode> builder)
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.CodeHash).IsRequired();
            builder.Property(x => x.ClientId).IsRequired().HasMaxLength(2048);
            builder.Property(x => x.ClientName).IsRequired().HasMaxLength(100);
            builder.Property(x => x.RedirectUri).IsRequired().HasMaxLength(2048);
            builder.Property(x => x.CodeChallenge).IsRequired().HasMaxLength(128);
            builder.HasIndex(x => x.CodeHash).IsUnique();
        }
    }
}
