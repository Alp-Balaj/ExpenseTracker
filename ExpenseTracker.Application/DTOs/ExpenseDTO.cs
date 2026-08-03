using ExpenseTracker.Application.Validation;
using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Application.DTOs
{
    public class ExpenseDTO
    {
        public Guid? Id { get; set; }

        [Required, NotWhitespace, MaxLength(100)]
        public required string Title { get; set; }

        [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
        public decimal Amount { get; set; }

        [NotDefault]
        public DateOnly Date { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        [NotDefault]
        public Guid AccountId { get; set; }

        [NotDefault]
        public Guid CategoryId { get; set; }

        /// <summary>
        /// Stream R. What other people owe the user on this expense; null or
        /// absent means it is not a split bill. Client-writable: this is the one
        /// reimbursement figure a request may set.
        /// <para>
        /// <see cref="Amount"/> stays gross either way — a €60 dinner where €20
        /// is expected back is <c>Amount = 60, ExpectedReimbursement = 20</c>,
        /// and the account is still debited 60.
        /// </para>
        /// </summary>
        public decimal? ExpectedReimbursement { get; set; }

        /// <summary>
        /// Response only: Σ of what has actually come back. Server-owned and
        /// ignored on write, the same treatment <see cref="AccountDTO.Balance"/>
        /// gets — it moves only through the settle endpoint.
        /// </summary>
        public decimal SettledReimbursement { get; set; }

        /// <summary>Response only: still out, expected − settled.</summary>
        public decimal OpenReimbursement { get; set; }
    }
}
