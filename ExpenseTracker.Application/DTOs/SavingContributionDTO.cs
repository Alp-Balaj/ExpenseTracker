using ExpenseTracker.Application.Validation;
using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Application.DTOs
{
    public class SavingContributionDTO
    {
        public Guid? Id { get; set; }

        // No [NotDefault]: POST api/SavingContribution/{goalId} takes the goal
        // from the route and legitimately leaves the body's GoalId empty.
        public Guid GoalId { get; set; }

        [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
        public decimal Amount { get; set; }

        [NotDefault]
        public DateOnly Date { get; set; }

        [MaxLength(500)]
        public string? Note { get; set; }

    }
}
