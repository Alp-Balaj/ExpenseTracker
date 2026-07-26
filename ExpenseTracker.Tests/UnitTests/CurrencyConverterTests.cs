using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Services;
using Xunit;

namespace ExpenseTracker.Tests.UnitTests;

// Locks in Fix-10: mixed-currency totals must be converted via each currency's
// rate into the base currency, never summed raw.
public sealed class CurrencyConverterTests
{
    [Fact]
    public void Single_currency_matching_base_sums_directly()
    {
        var converter = new CurrencyConverter("USD", new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = 1m
        });

        var result = converter.Summarize(new[]
        {
            new CurrencyAmount("USD", 10m),
            new CurrencyAmount("USD", 5m)
        });

        Assert.Equal(15m, result.ConvertedTotal);
        Assert.False(result.HasMissingRates);
        Assert.Single(result.Subtotals);
    }

    [Fact]
    public void Mixed_currencies_convert_each_amount_via_its_own_rate_not_raw_sum()
    {
        // EUR->USD at 1.10: 10 EUR = 11 USD, plus 5 USD = 16 USD total.
        // A raw (bugged) sum would read 15.
        var converter = new CurrencyConverter("USD", new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = 1m,
            ["EUR"] = 1.10m
        });

        var result = converter.Summarize(new[]
        {
            new CurrencyAmount("EUR", 10m),
            new CurrencyAmount("USD", 5m)
        });

        Assert.Equal(16m, result.ConvertedTotal);
        Assert.False(result.HasMissingRates);
        Assert.Equal(2, result.Subtotals.Count);
        Assert.Equal(10m, result.Subtotals.Single(s => s.Currency == "EUR").Total);
        Assert.Equal(5m, result.Subtotals.Single(s => s.Currency == "USD").Total);
    }

    [Fact]
    public void Missing_rate_for_a_non_base_currency_flags_and_suppresses_converted_total()
    {
        var converter = new CurrencyConverter("USD", new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = 1m
            // No rate for GBP.
        });

        var result = converter.Summarize(new[]
        {
            new CurrencyAmount("GBP", 10m),
            new CurrencyAmount("USD", 5m)
        });

        Assert.True(result.HasMissingRates);
        Assert.Null(result.ConvertedTotal);
    }

    [Fact]
    public void No_base_currency_configured_flags_missing_rates_even_with_one_currency()
    {
        var converter = new CurrencyConverter(null, new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = 1m
        });

        var result = converter.Summarize(new[] { new CurrencyAmount("USD", 10m) });

        Assert.True(result.HasMissingRates);
        Assert.Null(result.ConvertedTotal);
    }

    [Fact]
    public void Null_currency_on_an_amount_is_treated_as_already_in_base()
    {
        var converter = new CurrencyConverter("USD", new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = 1m
        });

        var result = converter.Summarize(new[] { new CurrencyAmount(null, 42m) });

        Assert.Equal(42m, result.ConvertedTotal);
        Assert.False(result.HasMissingRates);
    }
}
