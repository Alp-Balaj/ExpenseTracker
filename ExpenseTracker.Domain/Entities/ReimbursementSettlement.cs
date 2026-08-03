using System.Diagnostics.CodeAnalysis;
using ExpenseTracker.Domain.Entities.Common;

namespace ExpenseTracker.Domain.Entities
{
    /// <summary>
    /// Stream R: one instalment of a <see cref="Reimbursement"/> actually coming
    /// back, landing on an account the user picks at settle time.
    /// <para>
    /// A settlement is <b>not income</b>. It credits an account and closes part
    /// of a receivable the user was already owed; income statistics never see
    /// it, and no <see cref="Income"/> row is ever written for one.
    /// </para>
    /// </summary>
    public class ReimbursementSettlement : BaseEntity
    {
        [SetsRequiredMembers]
        public ReimbursementSettlement() : base()
        {
            UserId = null!;
            ReimbursementId = default!;
            AccountId = default!;
            Amount = default!;
            Date = default!;

            Reimbursement = null!;
            Account = null!;
        }

        public required Guid ReimbursementId { get; set; }
        public required Reimbursement Reimbursement { get; set; }

        /// <summary>Any account the user owns; chosen per settlement, not fixed to the expense's.</summary>
        public required Guid AccountId { get; set; }
        public required Account Account { get; set; }

        public required decimal Amount { get; set; }
        public required DateOnly Date { get; set; }
    }
}
