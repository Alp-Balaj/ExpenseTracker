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

        // Signed since Stream V: positive reserves, negative releases. Zero is
        // the one amount that means nothing, and [NotDefault] is exactly the
        // rule "any value but the default" for a decimal.
        [NotDefault]
        public decimal Amount { get; set; }

        [NotDefault]
        public DateOnly Date { get; set; }

        [MaxLength(500)]
        public string? Note { get; set; }

        /// <summary>
        /// The account this money is reserved against. Required on every write
        /// path even though the column is nullable — see
        /// <see cref="Domain.Entities.SavingContribution.AccountId"/>.
        /// </summary>
        [NotDefault]
        public Guid? AccountId { get; set; }
    }
}
