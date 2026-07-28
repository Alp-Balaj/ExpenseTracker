using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Entities.Common;
using ExpenseTracker.Domain.Entities.User;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseTracker.Infrastructure.Persistence
{
    public class ApplicationDbContext : IdentityDbContext<User>
    {
        public ApplicationDbContext(DbContextOptions options)
            : base(options)
        {
        }

        public DbSet<Account> Accounts { get; set; }
        public DbSet<Budget> Budgets { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<Currency> Currencies { get; set; }
        public DbSet<Expense> Expenses { get; set; }
        public DbSet<FutureExpense> FutureExpenses { get; set; }
        public DbSet<Income> Incomes { get; set; }
        public DbSet<SavingGoal> SavingGoals { get; set; }
        public DbSet<SavingContribution> SavingContributions { get; set; }
        public DbSet<Transfer> Transfers { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<UserPreferences> UserPreferences { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

            ConfigureUserOwnership(modelBuilder);
        }

        /// <summary>
        /// Two rules that hold for every <see cref="BaseEntity"/>, applied here
        /// rather than in each configuration so a new user-owned entity cannot
        /// silently miss them:
        /// <list type="bullet">
        /// <item>UserId is a real foreign key to the Identity user, cascading on
        /// delete — it was a bare string, so a deleted user left every row they
        /// ever created behind, unreachable and uncounted.</item>
        /// <item>UserId is indexed, because it is the one predicate every single
        /// query in the application filters on.</item>
        /// </list>
        /// </summary>
        private static void ConfigureUserOwnership(ModelBuilder modelBuilder)
        {
            var userOwned = modelBuilder.Model
                .GetEntityTypes()
                .Where(e => typeof(BaseEntity).IsAssignableFrom(e.ClrType))
                .ToList();

            foreach (var entityType in userOwned)
            {
                var entity = modelBuilder.Entity(entityType.ClrType);

                entity.HasOne(typeof(User))
                      .WithMany()
                      .HasForeignKey(nameof(BaseEntity.UserId))
                      .OnDelete(DeleteBehavior.Cascade);

                // An index that already leads with UserId (Expenses' (UserId,
                // Date), Currencies' (UserId, Code), ...) answers this predicate
                // too — a second one on the column alone would only add write
                // cost.
                var covered = entityType.GetIndexes()
                    .Any(index => index.Properties[0].Name == nameof(BaseEntity.UserId));

                if (!covered)
                    entity.HasIndex(nameof(BaseEntity.UserId));
            }
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            StampUpdatedAt();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            StampUpdatedAt();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void StampUpdatedAt()
        {
            foreach (var entry in ChangeTracker.Entries<BaseEntity>())
            {
                if (entry.State == EntityState.Modified)
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
            }
        }
    }
}
