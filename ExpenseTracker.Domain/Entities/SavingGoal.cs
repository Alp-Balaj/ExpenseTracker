using System.Diagnostics.CodeAnalysis;
using ExpenseTracker.Domain.Entities.Common;

namespace ExpenseTracker.Domain.Entities
{
    public class SavingGoal : BaseEntity
    {
        [SetsRequiredMembers]
        public SavingGoal() : base()
        {
            UserId = null!;
            Title = default!;
            TargetAmount = default!;
            SavedAmount = default!;
            Deadline = default!;

            AccountId = default!;
            CategoryId = default!;
            Account = null!;
            Category = null!;
        }
        public required string Title { get; set; }
        public required decimal TargetAmount { get; set; }
        public required decimal SavedAmount { get; set; }
        public required DateTime Deadline { get; set; }
        public string? Description { get; set; }

        public required Guid AccountId { get; set; }
        public required Guid CategoryId { get; set; }
        public required Account Account { get; set; }
        public required Category Category { get; set; }
        public ICollection<SavingContribution> SavingContributions { get; set; } = new List<SavingContribution>();
        
    }
}