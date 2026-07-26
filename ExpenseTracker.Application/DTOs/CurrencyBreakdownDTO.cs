namespace ExpenseTracker.Application.DTOs
{
    /// <summary>One amount tagged with its currency code (null = already in the base currency).</summary>
    public sealed record CurrencyAmount(string? Currency, decimal Amount);

    public sealed class CurrencySubtotalDTO
    {
        public required string Currency { get; init; }
        public decimal Total { get; init; }
    }

    /// <summary>
    /// Per-currency subtotals plus, when every rate is known, a grand total converted
    /// into the user's base currency. When <see cref="HasMissingRates"/> is true,
    /// <see cref="ConvertedTotal"/> is null and only <see cref="Subtotals"/> are reliable.
    /// </summary>
    public sealed class CurrencyBreakdown
    {
        public IReadOnlyList<CurrencySubtotalDTO> Subtotals { get; init; } = [];
        public string? BaseCurrency { get; init; }
        public decimal? ConvertedTotal { get; init; }
        public bool HasMissingRates { get; init; }
    }
}
