using ExpenseTracker.Application.Validation;
using ExpenseTracker.Shared.Enums;
using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Application.DTOs
{
    public class CategoryDTO
    {
        public Guid? Id { get; set; }

        [Required, NotWhitespace, MaxLength(50)]
        public required string Name { get; set; }

        [MaxLength(200)]
        public string? Description { get; set; }

        [EnumDataType(typeof(CategoryType))]
        public int CategoryType { get; set; }

        [MaxLength(20)]
        public string? Color { get; set; }
    }

    public class CategorySummaryDTO
    {
        public Guid? Id { get; set; }
        public required string Name { get; set; }
        public string? Description { get; set; }
        public int CategoryType { get; set; }

        /// <summary>
        /// Legacy raw sum across all currencies — wrong when a category mixes currencies.
        /// Kept for backward compatibility; prefer <see cref="Subtotals"/> and
        /// <see cref="ConvertedTotal"/>.
        /// </summary>
        public decimal TotalAmount { get; set; }

        public string? Color { get; set; }

        /// <summary>Per-currency totals for this category.</summary>
        public IReadOnlyList<CurrencySubtotalDTO> Subtotals { get; set; } = [];

        /// <summary>The user's base currency code, or null when unset/unavailable.</summary>
        public string? BaseCurrency { get; set; }

        /// <summary>
        /// Total converted into <see cref="BaseCurrency"/> (amountInBase = amount *
        /// ExchangeRateToBase). Null when the base currency is unset or a rate is missing.
        /// </summary>
        public decimal? ConvertedTotal { get; set; }

        /// <summary>True when conversion was impossible for at least one currency.</summary>
        public bool HasMissingRates { get; set; }
    }
}
