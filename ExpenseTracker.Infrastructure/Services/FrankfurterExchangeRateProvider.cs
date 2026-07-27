using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExpenseTracker.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ExpenseTracker.Infrastructure.Services;

/// <summary>
/// Reads ECB reference rates from frankfurter.app — free, keyless, and the reason
/// Sugg-8 introduces no new secret to manage.
///
/// Direction, which is the whole point of this class: the upstream answers
/// <c>?base=EUR</c> with "1 EUR = 1.1389 USD", i.e. units of the quoted currency per
/// one base unit. This app stores the inverse (Fix-10): <c>amountInBase = amount *
/// ExchangeRateToBase</c>, so USD's stored rate must be 1/1.1389 = 0.877865 EUR per
/// dollar. Every rate handed back by this provider has already been flipped.
/// </summary>
public sealed class FrankfurterExchangeRateProvider : IExchangeRateProvider
{
    public const string HttpClientName = "exchange-rates";

    // Matches the decimal(18,6) column, so the value that round-trips through the
    // database is the value the tests and the UI reason about — no silent truncation
    // between "what we computed" and "what got stored".
    private const int RateScale = 6;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FrankfurterExchangeRateProvider> _logger;

    public FrankfurterExchangeRateProvider(
        IHttpClientFactory httpClientFactory,
        ILogger<FrankfurterExchangeRateProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ExchangeRateSnapshot?> GetLatestRatesAsync(
        string baseCurrency, CancellationToken cancellationToken = default)
    {
        var code = baseCurrency?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code))
            return null;

        FrankfurterResponse? payload;
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);

            // No `symbols` filter on purpose: the endpoint silently drops symbols it
            // doesn't publish, so a filtered request can't distinguish "not covered"
            // from "typo". Asking for the full table (~30 currencies) and picking
            // locally is one round trip either way and lets the updater report
            // exactly which of the user's currencies the ECB has no quote for.
            using var response = await client.GetAsync(
                $"latest?base={Uri.EscapeDataString(code)}", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // 404 is how the upstream says "I don't cover this currency" (ALL, RSD,
                // ...). Not an error worth alarming about: manual entry stays the
                // documented fallback for those.
                _logger.LogWarning(
                    "Exchange rate fetch for base {BaseCurrency} returned {StatusCode}; keeping existing rates.",
                    code, (int)response.StatusCode);
                return null;
            }

            payload = await response.Content.ReadFromJsonAsync<FrankfurterResponse>(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                                   && !cancellationToken.IsCancellationRequested)
        {
            // Network down, DNS gone, upstream nonsense, client timeout — all mean the
            // same thing to the caller: keep yesterday's rates and try again tomorrow.
            _logger.LogWarning(ex,
                "Exchange rate fetch for base {BaseCurrency} failed; keeping existing rates.", code);
            return null;
        }

        if (payload?.Rates is null || payload.Rates.Count == 0)
        {
            _logger.LogWarning("Exchange rate fetch for base {BaseCurrency} returned no rates.", code);
            return null;
        }

        var ratesToBase = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var (symbol, unitsPerBase) in payload.Rates)
        {
            // A zero or negative quote would invert to infinity or a negative rate and
            // poison every converted total; drop it and let the old rate stand.
            if (unitsPerBase <= 0m)
                continue;

            ratesToBase[symbol] = Math.Round(1m / unitsPerBase, RateScale, MidpointRounding.AwayFromZero);
        }

        // The base currency is 1 against itself by definition and the upstream never
        // includes it in its own rate table.
        ratesToBase[code] = 1m;

        var asOf = DateOnly.TryParse(payload.Date, out var parsed)
            ? parsed
            : DateOnly.FromDateTime(DateTime.UtcNow);

        _logger.LogInformation(
            "Fetched {Count} exchange rates against {BaseCurrency}, quoted {AsOf:yyyy-MM-dd}.",
            ratesToBase.Count, code, asOf);

        return new ExchangeRateSnapshot(code, asOf, ratesToBase);
    }

    private sealed class FrankfurterResponse
    {
        [JsonPropertyName("base")]
        public string? Base { get; set; }

        [JsonPropertyName("date")]
        public string? Date { get; set; }

        [JsonPropertyName("rates")]
        public Dictionary<string, decimal>? Rates { get; set; }
    }
}
