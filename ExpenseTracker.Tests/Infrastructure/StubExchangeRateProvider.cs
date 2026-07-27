using ExpenseTracker.Application.Interfaces;

namespace ExpenseTracker.Tests.Infrastructure;

/// <summary>
/// Stands in for the real frankfurter.app provider so the suite never touches the
/// network. Registered as a singleton by <see cref="CustomWebApplicationFactory"/>,
/// which lets a test rewrite what the "upstream" publishes before calling the refresh
/// endpoint. Tests in a collection run sequentially, so the shared mutable state is
/// safe as long as each test uses its own base currency code.
/// </summary>
public sealed class StubExchangeRateProvider : IExchangeRateProvider
{
    /// <summary>
    /// Base code → what the upstream would answer. A base that is absent (or mapped to
    /// null) models "the upstream does not cover this currency", i.e. the 404 path.
    /// </summary>
    public Dictionary<string, ExchangeRateSnapshot?> Snapshots { get; } = new(StringComparer.OrdinalIgnoreCase);

    private int _callCount;
    public int CallCount => Volatile.Read(ref _callCount);

    public StubExchangeRateProvider()
    {
        // Real ECB quotes for 2026-07-27, already inverted into this app's direction
        // (upstream said 1 EUR = 1.1389 USD, so a dollar is 1/1.1389 = 0.878040 EUR).
        Snapshots["EUR"] = new ExchangeRateSnapshot(
            "EUR",
            new DateOnly(2026, 7, 27),
            new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["EUR"] = 1m,
                ["USD"] = 0.878040m,
                ["GBP"] = 1.169262m,
                ["SEK"] = 0.090543m
            });
    }

    public Task<ExchangeRateSnapshot?> GetLatestRatesAsync(
        string baseCurrency, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult(
            Snapshots.TryGetValue(baseCurrency, out var snapshot) ? snapshot : null);
    }
}
