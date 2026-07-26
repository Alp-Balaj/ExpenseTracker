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

        [NotDefault]
        public Guid AccountId { get; set; }

        [NotDefault]
        public Guid CategoryId { get; set; }
    }
}
