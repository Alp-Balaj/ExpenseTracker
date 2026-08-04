using ExpenseTracker.Application.Validation;
using ExpenseTracker.Shared.Enums;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

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

        /// <summary>
        /// Cycle U W2: set only on the reserved "Shared" category, so the UI can
        /// badge it and hide its delete button.
        ///
        /// <para>
        /// <b>Read-only.</b> <c>CategoryMapping</c> writes it out and never maps
        /// it back in, so a client cannot promote its own category into a system
        /// one, nor demote the system one to slip past the delete guard.
        /// </para>
        ///
        /// <para>
        /// <b>Omitted from the payload when null</b>, which is every category a
        /// user made and therefore every category a user in no pots has. That
        /// keeps the category list byte-for-byte what it was before this wave
        /// for anybody who has never joined a pot — the cycle's second rule,
        /// which a uniformly-added <c>"systemKind": null</c> would have broken
        /// for every user in the application to describe one row that only some
        /// of them have.
        /// </para>
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public CategorySystemKind? SystemKind { get; set; }
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

        /// <summary>
        /// Cycle U W2. Set only on the reserved "Shared" category and omitted
        /// entirely otherwise — see <see cref="CategoryDTO.SystemKind"/> for why
        /// the null case has to leave no trace in the payload.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public CategorySystemKind? SystemKind { get; set; }

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
