using ExpenseTracker.Application.Validation;
using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Application.DTOs
{
    public class FutureExpenseDTO
    {
        public Guid? Id { get; set; }

        [Required, NotWhitespace, MaxLength(100)]
        public required string Title { get; set; }

        [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
        public decimal Amount { get; set; }

        [NotDefault]
        public DateOnly? Date { get; set; }

        [NotDefault]
        public Guid CategoryId { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }
    }
}
