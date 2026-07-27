namespace ExpenseTracker.Application.Interfaces
{
    /// <summary>What one refresh did, for the API response and the log line.</summary>
    /// <param name="BaseCurrency">The base the rates were fetched against.</param>
    /// <param name="RatesAsOf">The upstream's quote date, null when nothing was fetched.</param>
    /// <param name="UpdatedCount">Rows whose rate was written.</param>
    /// <param name="UnsupportedCodes">Currencies the user holds that the upstream does not
    /// publish (exotic codes, ECB gaps). Their rates are left exactly as they were —
    /// manual entry is the fallback for these.</param>
    /// <param name="ProviderUnavailable">True when the fetch failed or the base currency
    /// itself is not covered. Every existing rate survives untouched.</param>
    public sealed record ExchangeRateUpdateResult(
        string BaseCurrency,
        DateOnly? RatesAsOf,
        int UpdatedCount,
        IReadOnlyList<string> UnsupportedCodes,
        bool ProviderUnavailable);

    /// <summary>
    /// Refreshes stored <c>Currency.ExchangeRateToBase</c> values. Deliberately takes an
    /// explicit user id rather than reading the current user: the daily background service
    /// runs with no HTTP context and must sweep every user.
    /// </summary>
    public interface IExchangeRateUpdater
    {
        Task<ExchangeRateUpdateResult> UpdateUserAsync(string userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Refreshes every user who actually holds currencies. Users sharing a base
        /// currency share a single upstream call.
        /// </summary>
        Task<int> UpdateAllUsersAsync(CancellationToken cancellationToken = default);
    }
}
