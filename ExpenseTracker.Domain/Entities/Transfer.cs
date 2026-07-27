using System.Diagnostics.CodeAnalysis;
using ExpenseTracker.Domain.Entities.Common;

namespace ExpenseTracker.Domain.Entities
{
    /// <summary>
    /// Money moved between two of the user's own accounts. A transfer is
    /// deliberately *not* an expense plus an income: it lives in its own table
    /// so it never reaches the income/expense totals, and both halves move or
    /// disappear together.
    /// <para>
    /// v1 only allows accounts that share a currency, so a single
    /// <see cref="Amount"/> describes both sides of the move.
    /// </para>
    /// </summary>
    public class Transfer : BaseEntity
    {
        [SetsRequiredMembers]
        public Transfer() : base()
        {
            UserId = null!;
            Amount = default!;
            Date = default!;

            FromAccountId = default!;
            ToAccountId = default!;
            FromAccount = null!;
            ToAccount = null!;
        }

        public required decimal Amount { get; set; }
        public required DateOnly Date { get; set; }
        public string? Note { get; set; }

        public required Guid FromAccountId { get; set; }
        public required Guid ToAccountId { get; set; }
        public required Account FromAccount { get; set; }
        public required Account ToAccount { get; set; }

        /// <summary>
        /// Optional savings goal this transfer counts toward. Only goals backed
        /// by <see cref="ToAccountId"/> are eligible — the money has to actually
        /// land in the account behind the goal.
        /// </summary>
        public Guid? SavingGoalId { get; set; }
        public SavingGoal? SavingGoal { get; set; }

        /// <summary>
        /// The contribution row this transfer created for <see cref="SavingGoalId"/>,
        /// so an edit or delete can reverse the goal's SavedAmount. Server-owned:
        /// clients never set it. Nulled by the database if the contribution is
        /// deleted on its own, which downgrades the link without touching the money.
        /// </summary>
        public Guid? SavingContributionId { get; set; }
        public SavingContribution? SavingContribution { get; set; }
    }
}
