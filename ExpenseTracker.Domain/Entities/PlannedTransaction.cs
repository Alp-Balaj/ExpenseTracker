using System.Diagnostics.CodeAnalysis;
using ExpenseTracker.Domain.Entities.Common;
using ExpenseTracker.Shared.Enums;

namespace ExpenseTracker.Domain.Entities
{
    /// <summary>
    /// Money the user already knows is coming: rent, a subscription, a salary,
    /// the standing order into savings, or a single future-dated payment.
    /// <para>
    /// This is a <em>template</em>, not a transaction. It never touches an
    /// account balance. The sweep turns each due date into a
    /// <see cref="PlannedOccurrence"/>, and only committing one of those
    /// creates a real Expense / Income / Transfer.
    /// </para>
    /// <para>
    /// Replaces the old <c>FutureExpense</c>, which could only describe a
    /// one-off expense, carried no account, and used a category axis of its
    /// own that no posted row could ever have belonged to.
    /// </para>
    /// </summary>
    public class PlannedTransaction : BaseEntity
    {
        [SetsRequiredMembers]
        public PlannedTransaction() : base()
        {
            UserId = null!;
            Title = null!;
            Amount = default!;
            Kind = default!;
            Recurrence = default!;
            StartDate = default!;
        }

        public required string Title { get; set; }

        /// <summary>
        /// What this usually costs. An estimate, not a commitment — the
        /// commit dialog pre-fills it and the user overrides it when the
        /// electricity bill lands. Only ever used to prefill and to total up
        /// what is coming; the posted row carries the real figure.
        /// </summary>
        public required decimal Amount { get; set; }

        public string? Description { get; set; }

        public required PlannedKind Kind { get; set; }

        /// <summary>
        /// Null for <see cref="PlannedKind.Transfer"/> (transfers have no
        /// category anywhere in the model); required otherwise, and its
        /// <see cref="CategoryType"/> must match <see cref="Kind"/> so a
        /// committed row lands in the same category as one typed by hand —
        /// which is what lets budgets and reports see it.
        /// </summary>
        public Guid? CategoryId { get; set; }
        public Category? Category { get; set; }

        /// <summary>
        /// Where the money comes from — the debited account for an expense or
        /// a transfer. Optional: it is only a default for the commit dialog,
        /// because the phone bill is sometimes paid in cash and sometimes
        /// from the bank, and the app should not force a lie either way.
        /// </summary>
        public Guid? FromAccountId { get; set; }
        public Account? FromAccount { get; set; }

        /// <summary>
        /// Where the money lands — the credited account for an income or a
        /// transfer. Optional, for the same reason as
        /// <see cref="FromAccountId"/>.
        /// </summary>
        public Guid? ToAccountId { get; set; }
        public Account? ToAccount { get; set; }

        /// <summary>
        /// Optional savings goal a committed transfer contributes to. Only
        /// meaningful for <see cref="PlannedKind.Transfer"/>, and only when
        /// the goal is backed by <see cref="ToAccountId"/> — the same rule
        /// <c>TransferController</c> enforces for a hand-entered transfer.
        /// </summary>
        public Guid? SavingGoalId { get; set; }
        public SavingGoal? SavingGoal { get; set; }

        public required Recurrence Recurrence { get; set; }

        /// <summary>
        /// The first due date, and the anchor every later one is derived from:
        /// its day-of-week for weekly, its day-of-month for monthly, its
        /// month and day for yearly.
        /// </summary>
        public required DateOnly StartDate { get; set; }

        /// <summary>
        /// Last date that may still come due, inclusive. Null means "until I
        /// say otherwise". Ignored for <see cref="Recurrence.Once"/>.
        /// </summary>
        public DateOnly? EndDate { get; set; }

        /// <summary>
        /// Paused templates generate nothing new. Occurrences already pending
        /// survive a pause — they describe money that was genuinely due, and
        /// silently dropping them would lose a bill.
        /// </summary>
        public bool IsActive { get; set; } = true;

        public ICollection<PlannedOccurrence> Occurrences { get; set; } = new List<PlannedOccurrence>();
    }
}
