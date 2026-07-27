using ExpenseTracker.Application.Validation;
using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Application.DTOs
{
    public class TransferDTO
    {
        public Guid? Id { get; set; }

        [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
        public decimal Amount { get; set; }

        [NotDefault]
        public DateOnly Date { get; set; }

        [MaxLength(500)]
        public string? Note { get; set; }

        [NotDefault]
        public Guid FromAccountId { get; set; }

        [NotDefault]
        public Guid ToAccountId { get; set; }

        /// <summary>
        /// Optional. When set, the goal must be backed by <see cref="ToAccountId"/>
        /// and the transfer also books a contribution against it.
        /// </summary>
        [NotDefault]
        public Guid? SavingGoalId { get; set; }

        /// <summary>
        /// Read-only: the contribution the server booked for <see cref="SavingGoalId"/>.
        /// Ignored on the way in.
        /// </summary>
        public Guid? SavingContributionId { get; set; }
    }
}
