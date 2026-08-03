using ExpenseTracker.Application.Validation;
using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Application.DTOs
{
    public class SavingGoalDTO
    {
        public Guid? Id { get; set; }

        [Required, NotWhitespace, MaxLength(200)]
        public required string Title { get; set; }

        [Range(0.01, double.MaxValue, ErrorMessage = "TargetAmount must be greater than zero.")]
        public decimal TargetAmount { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "SavedAmount cannot be negative.")]
        public decimal SavedAmount { get; set; }

        [NotDefault]
        public DateOnly Deadline { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        /// <summary>
        /// Optional since Stream V: a goal is no longer tied to one account.
        /// Kept as the account the contribution form suggests first.
        /// </summary>
        public Guid? AccountId { get; set; }

        [NotDefault]
        public Guid CategoryId { get; set; }

        /// <summary>
        /// Server-derived, never accepted from a client: the currency of the
        /// goal's first contribution's account, and therefore the only currency
        /// further contributions may be made in. Null while a goal has no
        /// contributions — at which point any account may fund it.
        /// </summary>
        public string? CurrencyCode { get; set; }

        /// <summary>
        /// The goal's history, newest first — served on list and read
        /// endpoints, empty on the read-back after a write. Each row names the
        /// account its money is reserved against, which is what lets a goal
        /// card show where the money actually sits.
        /// </summary>
        public List<SavingContributionDTO> Contributions { get; set; } = [];
    }
}
