using System.Diagnostics.CodeAnalysis;
using ExpenseTracker.Domain.Entities.Common;

namespace ExpenseTracker.Domain.Entities
{
    public class SavingContribution : BaseEntity
    {
        [SetsRequiredMembers]
        public SavingContribution() : base()
        {
            GoalId = default!;

            Amount = default!;
            Date = default!;
            Note = default!;

            SavingGoal = null!;
        }
        public required Guid GoalId { get; set; }
        public required decimal Amount { get; set; }
        public required DateOnly Date { get; set; }
        public string? Note { get; set; }

        public required SavingGoal SavingGoal { get; set; }
    }
}