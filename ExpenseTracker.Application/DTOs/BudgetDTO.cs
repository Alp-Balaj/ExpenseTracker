using ExpenseTracker.Application.Validation;
using ExpenseTracker.Shared.Enums;
using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Application.DTOs
{
    public class BudgetDTO
    {
        public Guid? Id { get; set; }

        [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
        public decimal Amount { get; set; }

        [EnumDataType(typeof(BudgetPeriod))]
        public int Period { get; set; } = (int)BudgetPeriod.Monthly;

        [MaxLength(200)]
        public string? Note { get; set; }

        /// <summary>Null = overall budget. A non-null id must belong to the caller.</summary>
        [NotDefault]
        public Guid? CategoryId { get; set; }
    }

    /// <summary>A budget evaluated against one month's spend.</summary>
    public sealed class BudgetStatusDTO
    {
        public Guid Id { get; init; }
        public Guid? CategoryId { get; init; }

        /// <summary>Category name, or "Overall" for the all-categories budget.</summary>
        public required string CategoryName { get; init; }
        public string? CategoryColor { get; init; }
        public string? Note { get; init; }
        public int Period { get; init; }

        /// <summary>The month this status was computed for, as yyyy-MM.</summary>
        public required string Month { get; init; }

        /// <summary>The limit, in <see cref="BaseCurrency"/>.</summary>
        public decimal Limit { get; init; }

        /// <summary>Spend for the month converted into <see cref="BaseCurrency"/>.</summary>
        public decimal Spent { get; init; }

        /// <summary>Limit minus spent; negative once the budget is overspent.</summary>
        public decimal Remaining { get; init; }

        /// <summary>Spent as a percentage of the limit; uncapped, so overspend reads above 100.</summary>
        public decimal PercentUsed { get; init; }

        public string? BaseCurrency { get; init; }

        /// <summary>Per-currency breakdown of the raw spend behind <see cref="Spent"/>.</summary>
        public IReadOnlyList<CurrencySubtotalDTO> Subtotals { get; init; } = [];

        /// <summary>
        /// True when at least one expense could not be converted into the base currency;
        /// <see cref="Spent"/> then excludes it and understates the real spend.
        /// </summary>
        public bool HasMissingRates { get; init; }
    }
}
