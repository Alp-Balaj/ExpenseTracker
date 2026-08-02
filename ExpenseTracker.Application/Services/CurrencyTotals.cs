using ExpenseTracker.Application.DTOs;

namespace ExpenseTracker.Application.Services;

/// <summary>
/// Summing helper for the analytics services. Where a budget's "spent" figure
/// follows the all-or-nothing <see cref="CurrencyConverter.Summarize"/> total,
/// a comparison or a projection is better served by the convertible part: one
/// exotic currency without a rate should dent the figure, not zero it.
/// </summary>
internal static class CurrencyTotals
{
    /// <summary>
    /// Converts each amount into the base currency and sums the ones that can
    /// be converted; the ones that cannot are skipped and flagged through
    /// <paramref name="hasMissingRates"/>.
    /// </summary>
    public static decimal SumConvertible(
        CurrencyConverter converter,
        IEnumerable<CurrencyAmount> items,
        ref bool hasMissingRates)
    {
        decimal total = 0m;

        foreach (var item in items)
        {
            var one = converter.Summarize([item]);
            if (one.ConvertedTotal is decimal converted)
                total += converted;
            else
                hasMissingRates = true;
        }

        return total;
    }
}
