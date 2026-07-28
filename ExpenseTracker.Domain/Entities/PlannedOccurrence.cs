using System.Diagnostics.CodeAnalysis;
using ExpenseTracker.Domain.Entities.Common;
using ExpenseTracker.Shared.Enums;

namespace ExpenseTracker.Domain.Entities
{
    /// <summary>
    /// One due date of a <see cref="PlannedTransaction"/> — August's rent, this
    /// week's shop — sitting in the queue until the user commits or skips it.
    /// <para>
    /// A row per occurrence rather than a "next due date" pointer on the
    /// template, because the queue has to survive absence: three months away
    /// means three separate rents to deal with individually, and a pointer can
    /// only ever say "overdue" once.
    /// </para>
    /// <para>
    /// <see cref="PeriodKey"/> plus the template id is unique, and that
    /// constraint <em>is</em> the idempotency guard: generation runs on every
    /// read, so the only thing stopping a second August rent from appearing is
    /// the database refusing to store one.
    /// </para>
    /// </summary>
    public class PlannedOccurrence : BaseEntity
    {
        [SetsRequiredMembers]
        public PlannedOccurrence() : base()
        {
            UserId = null!;
            PlannedTransactionId = default!;
            PlannedTransaction = null!;
            PeriodKey = null!;
            DueDate = default!;
        }

        public required Guid PlannedTransactionId { get; set; }
        public required PlannedTransaction PlannedTransaction { get; set; }

        /// <summary>
        /// The period this occurrence covers, in a form that is stable no
        /// matter when generation happens to run: <c>2026-08</c> monthly,
        /// <c>2026-W32</c> weekly, <c>2026</c> yearly, <c>once</c> for a
        /// one-off. Derived, never user-supplied.
        /// </summary>
        public required string PeriodKey { get; set; }

        /// <summary>
        /// When the money is expected to move. A prediction — the committed
        /// row carries the date it actually moved, which is often a day or
        /// three later.
        /// </summary>
        public required DateOnly DueDate { get; set; }

        public PlannedOccurrenceState State { get; set; } = PlannedOccurrenceState.Pending;

        public DateTime? CommittedAt { get; set; }

        /// <summary>
        /// What this occurrence became. Exactly one is set once
        /// <see cref="State"/> is <see cref="PlannedOccurrenceState.Committed"/>,
        /// matching the template's kind.
        /// <para>
        /// Nulled by the database if the posted row is later deleted on its
        /// own — the same downgrade-don't-cascade rule <c>Transfer</c> uses for
        /// its savings contribution. The occurrence stays Committed: the money
        /// did move, and un-committing it here would offer to post it a second
        /// time.
        /// </para>
        /// </summary>
        public Guid? ExpenseId { get; set; }
        public Expense? Expense { get; set; }

        public Guid? IncomeId { get; set; }
        public Income? Income { get; set; }

        public Guid? TransferId { get; set; }
        public Transfer? Transfer { get; set; }
    }
}
