using System.Diagnostics.CodeAnalysis;
using ExpenseTracker.Domain.Entities.Common;

namespace ExpenseTracker.Domain.Entities
{
    /// <summary>
    /// Stream R: what other people owe the user on one expense — a split bill.
    /// <para>
    /// Deliberately amounts only. There is no debtor here, and none is coming:
    /// the feature is "I fronted money and some of it is still out", not a
    /// ledger of who owes whom.
    /// </para>
    /// <para>
    /// The expense itself is untouched — its <see cref="Expense.Amount"/> stays
    /// the gross figure that actually left the account. This row is what makes
    /// the analysis side able to count the user's own share instead.
    /// </para>
    /// </summary>
    public class Reimbursement : BaseEntity
    {
        [SetsRequiredMembers]
        public Reimbursement() : base()
        {
            UserId = null!;
            ExpenseId = default!;
            ExpectedAmount = default!;

            Expense = null!;
        }

        /// <summary>One reimbursement per expense at most — a unique index backs this.</summary>
        public required Guid ExpenseId { get; set; }
        public required Expense Expense { get; set; }

        /// <summary>
        /// What is expected back in total. Bounded by
        /// <c>0 &lt; ExpectedAmount &lt;= Expense.Amount</c>, enforced on the
        /// write path rather than by a check constraint so the violation can be
        /// answered with the sentence that explains it.
        /// </summary>
        public required decimal ExpectedAmount { get; set; }

        /// <summary>
        /// What has actually come back so far, in instalments. Open =
        /// ExpectedAmount − Σ Settlements.Amount.
        /// </summary>
        public ICollection<ReimbursementSettlement> Settlements { get; set; } = new List<ReimbursementSettlement>();
    }
}
