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
        public required DateOnly Deadline { get; set; }
        public string? Description { get; set; }

        /// <summary>
        /// The goal's original single backing account. <b>Deprecated by Stream
        /// V:</b> money is now reserved per contribution, so nothing in the
        /// money math reads this. It survives as the schema's record of where a
        /// pre-V goal's money sat, and as the account the contribution form
        /// suggests first. Optional since a goal no longer needs one.
        /// </summary>
        public Guid? AccountId { get; set; }
        public required Guid CategoryId { get; set; }
        public Account? Account { get; set; }
        public required Category Category { get; set; }
        public ICollection<SavingContribution> SavingContributions { get; set; } = new List<SavingContribution>();
        
    }
}