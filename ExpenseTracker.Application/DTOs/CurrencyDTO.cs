using ExpenseTracker.Application.Validation;
using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Application.DTOs
{
    public class CurrencyDTO
    {
        public Guid? Id { get; set; }

        [Required, NotWhitespace]
        public required string Name { get; set; }

        [Required, StringLength(3, MinimumLength = 3)]
        public required string Code { get; set; }

        [Required, NotWhitespace, MaxLength(5)]
        public required string Symbol { get; set; }

        [Range(0.000001, double.MaxValue, ErrorMessage = "ExchangeRateToBase must be greater than zero.")]
        public decimal ExchangeRateToBase { get; set; }
    }

    public class CurrencyDropdownDTO
    {
        public Guid? Id { get; set; }
        public required string Name { get; set; }
        public required string Code { get; set; }
        public required string Symbol { get; set; }
    }
}
