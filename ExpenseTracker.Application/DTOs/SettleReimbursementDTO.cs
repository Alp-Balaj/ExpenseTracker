using System.ComponentModel.DataAnnotations;
using ExpenseTracker.Application.Validation;

namespace ExpenseTracker.Application.DTOs
{
    /// <summary>
    /// "Someone paid me back this much, into this account, on this day."
    /// <para>
    /// The account is chosen per settlement — it does not have to be the one the
    /// expense was paid from — but it must be one the user owns and in the same
    /// currency as the expense's account.
    /// </para>
    /// </summary>
    public class SettleReimbursementDTO
    {
        [NotDefault]
        public Guid AccountId { get; set; }

        [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
        public decimal Amount { get; set; }

        [NotDefault]
        public DateOnly Date { get; set; }
    }
}
