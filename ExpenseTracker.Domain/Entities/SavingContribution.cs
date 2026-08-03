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

        /// <summary>
        /// Signed: a positive contribution reserves money, a negative one
        /// releases it back. The goal's <see cref="SavingGoal.SavedAmount"/> is
        /// the running sum of both.
        /// </summary>
        public required decimal Amount { get; set; }
        public required DateOnly Date { get; set; }
        public string? Note { get; set; }

        /// <summary>
        /// Which account this money is reserved against. Nullable in the
        /// database only because the column was added to a live table and
        /// backfilled in the same migration — every write path requires it, so
        /// a null means "a row that predates the backfill", which is none.
        /// </summary>
        public Guid? AccountId { get; set; }

        public required SavingGoal SavingGoal { get; set; }
        public Account? Account { get; set; }
    }
}