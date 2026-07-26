namespace ExpenseTracker.Application.DTOs.Reports;

public sealed class TransactionReportResult
{
    public IReadOnlyList<TransactionReportRow> Rows { get; init; } = [];

    /// <summary>
    /// Legacy raw sum across all rows regardless of currency — wrong whenever rows mix
    /// currencies. Kept for backward compatibility; prefer <see cref="Subtotals"/> and
    /// <see cref="ConvertedTotal"/>.
    /// </summary>
    public decimal Total { get; init; }

    /// <summary>Per-currency totals of <see cref="Rows"/>.</summary>
    public IReadOnlyList<CurrencySubtotalDTO> Subtotals { get; init; } = [];

    /// <summary>The user's base currency code, or null when unset/unavailable.</summary>
    public string? BaseCurrency { get; init; }

    /// <summary>
    /// Grand total converted into <see cref="BaseCurrency"/> via each currency's
    /// ExchangeRateToBase (amountInBase = amount * rate). Null when the base currency
    /// is unset or any involved currency has no usable rate — see <see cref="HasMissingRates"/>.
    /// </summary>
    public decimal? ConvertedTotal { get; init; }

    /// <summary>True when conversion was impossible for at least one currency.</summary>
    public bool HasMissingRates { get; init; }
}
