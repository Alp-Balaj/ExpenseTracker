using ExpenseTracker.Application.Validation;
using ExpenseTracker.Shared.Enums;
using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Application.DTOs
{
    public class PlannedTransactionDTO
    {
        public Guid? Id { get; set; }

        [Required, NotWhitespace, MaxLength(100)]
        public required string Title { get; set; }

        /// <summary>
        /// What this usually costs. Pre-fills the commit dialog; the committed
        /// row carries whatever the user actually confirmed.
        /// </summary>
        [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
        public decimal Amount { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        [EnumDataType(typeof(PlannedKind))]
        public PlannedKind Kind { get; set; }

        /// <summary>
        /// Required for Expense and Income, and its type must match
        /// <see cref="Kind"/>. Must be null for a transfer.
        /// </summary>
        [NotDefault]
        public Guid? CategoryId { get; set; }

        /// <summary>Debited account. Optional — only a default for the commit dialog.</summary>
        [NotDefault]
        public Guid? FromAccountId { get; set; }

        /// <summary>Credited account. Optional — only a default for the commit dialog.</summary>
        [NotDefault]
        public Guid? ToAccountId { get; set; }

        /// <summary>Transfers only. The goal a committed transfer contributes to.</summary>
        [NotDefault]
        public Guid? SavingGoalId { get; set; }

        [EnumDataType(typeof(Recurrence))]
        public Recurrence Recurrence { get; set; }

        [NotDefault]
        public DateOnly StartDate { get; set; }

        public DateOnly? EndDate { get; set; }

        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Read-only: when this next comes round, computed rather than stored so
        /// the look-ahead never disagrees with the schedule. Null once finished.
        /// </summary>
        public DateOnly? NextDueDate { get; set; }

        /// <summary>Read-only: how many of its occurrences are still waiting to be dealt with.</summary>
        public int PendingCount { get; set; }
    }
}
