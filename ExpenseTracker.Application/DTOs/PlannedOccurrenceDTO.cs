using ExpenseTracker.Shared.Enums;

namespace ExpenseTracker.Application.DTOs
{
    /// <summary>
    /// One entry in the due queue. Carries the template's details alongside it so
    /// the client can render and commit the queue without a second request and a
    /// join of its own.
    /// </summary>
    public class PlannedOccurrenceDTO
    {
        public Guid Id { get; set; }
        public Guid PlannedTransactionId { get; set; }

        public string PeriodKey { get; set; } = string.Empty;
        public DateOnly DueDate { get; set; }
        public PlannedOccurrenceState State { get; set; }
        public DateTime? CommittedAt { get; set; }

        /// <summary>What it became. Exactly one is set once committed.</summary>
        public Guid? ExpenseId { get; set; }
        public Guid? IncomeId { get; set; }
        public Guid? TransferId { get; set; }

        // ── From the template, so the queue renders standalone ──────────────

        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public PlannedKind Kind { get; set; }

        /// <summary>The expected amount — what the commit dialog pre-fills.</summary>
        public decimal Amount { get; set; }

        public Guid? CategoryId { get; set; }

        /// <summary>
        /// The accounts the commit dialog pre-fills: the template's defaults,
        /// which committing keeps up to date with whatever was last used.
        /// </summary>
        public Guid? FromAccountId { get; set; }
        public Guid? ToAccountId { get; set; }

        public Guid? SavingGoalId { get; set; }
    }
}
