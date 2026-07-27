namespace ExpenseTracker.Application.Interfaces
{
    /// <summary>
    /// One successful fetch from an upstream rate source, already translated into this
    /// app's direction so no caller has to think about which way the upstream publishes.
    /// </summary>
    /// <param name="BaseCurrency">The code every rate in <paramref name="RatesToBase"/> is expressed against.</param>
    /// <param name="AsOf">The date the upstream stamped on the quotes. The ECB only
    /// publishes on business days, so over a weekend this legitimately trails today.</param>
    /// <param name="RatesToBase">Code → multiplier such that
    /// <c>amountInBase = amount * rate</c>. Always contains <paramref name="BaseCurrency"/> itself at 1.</param>
    public sealed record ExchangeRateSnapshot(
        string BaseCurrency,
        DateOnly AsOf,
        IReadOnlyDictionary<string, decimal> RatesToBase);

    public interface IExchangeRateProvider
    {
        /// <summary>
        /// Fetches the latest published rates against <paramref name="baseCurrency"/>.
        /// Returns <c>null</c> — never throws — when the upstream is unreachable, returns
        /// nonsense, or does not cover that base currency at all. A null result means
        /// "keep the rates you already have"; it is a normal outcome, not an error.
        /// </summary>
        Task<ExchangeRateSnapshot?> GetLatestRatesAsync(string baseCurrency, CancellationToken cancellationToken = default);
    }
}
