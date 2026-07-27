using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExpenseTracker.Infrastructure.Services;

/// <summary>
/// Writes fetched rates onto the per-user <c>Currency</c> rows.
///
/// Goes to <see cref="ApplicationDbContext"/> directly rather than through
/// <c>IUserRelatedRepository</c>/<c>IUnitOfWork</c>: those scope every query to
/// <c>ICurrentUserServices.UserId</c>, which is read off the HTTP context and simply
/// does not exist on a background timer tick. The user id is therefore passed in
/// explicitly and every query below still filters by it, so the ownership guarantee
/// the repository provides is preserved, just enforced here instead.
///
/// Schema note (per the suggestion): rates stay per-user rows. Splitting a global
/// ExchangeRate table from the user's currency selection is the cleaner long-term
/// shape, but it buys nothing for a single-user personal tracker.
/// </summary>
public sealed class ExchangeRateUpdater : IExchangeRateUpdater
{
    /// <summary>Mirrors <c>UserPreferences.BaseCurrency</c>'s own default.</summary>
    private const string FallbackBaseCurrency = "EUR";

    private readonly ApplicationDbContext _context;
    private readonly IExchangeRateProvider _provider;
    private readonly ILogger<ExchangeRateUpdater> _logger;

    public ExchangeRateUpdater(
        ApplicationDbContext context,
        IExchangeRateProvider provider,
        ILogger<ExchangeRateUpdater> logger)
    {
        _context = context;
        _provider = provider;
        _logger = logger;
    }

    public Task<ExchangeRateUpdateResult> UpdateUserAsync(
        string userId, CancellationToken cancellationToken = default) =>
        UpdateUserAsync(userId, new Dictionary<string, ExchangeRateSnapshot?>(StringComparer.OrdinalIgnoreCase), cancellationToken);

    public async Task<int> UpdateAllUsersAsync(CancellationToken cancellationToken = default)
    {
        // Only users who actually hold currencies — there is nothing to refresh for
        // anyone else, and no reason to call the upstream on their behalf.
        var userIds = await _context.Currencies
            .Select(c => c.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        // One fetch per distinct base currency for the whole sweep, not one per user.
        var snapshots = new Dictionary<string, ExchangeRateSnapshot?>(StringComparer.OrdinalIgnoreCase);
        var updated = 0;

        foreach (var userId in userIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // One user's bad data (an unsupported base, say) must not abort the sweep
            // for everyone behind them in the list.
            try
            {
                var result = await UpdateUserAsync(userId, snapshots, cancellationToken);
                updated += result.UpdatedCount;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Exchange rate refresh failed for user {UserId}; continuing.", userId);
            }
        }

        return updated;
    }

    private async Task<ExchangeRateUpdateResult> UpdateUserAsync(
        string userId,
        IDictionary<string, ExchangeRateSnapshot?> snapshotCache,
        CancellationToken cancellationToken)
    {
        var baseCurrency = await _context.UserPreferences
            .Where(p => p.UserId == userId)
            .Select(p => p.BaseCurrency)
            .FirstOrDefaultAsync(cancellationToken);

        // A user who has never saved preferences has no row; UserPreferencesService
        // hands back the same default on read, so match it here.
        if (string.IsNullOrWhiteSpace(baseCurrency))
            baseCurrency = FallbackBaseCurrency;
        baseCurrency = baseCurrency.Trim().ToUpperInvariant();

        var currencies = await _context.Currencies
            .Where(c => c.UserId == userId)
            .ToListAsync(cancellationToken);

        if (currencies.Count == 0)
            return new ExchangeRateUpdateResult(baseCurrency, null, 0, Array.Empty<string>(), false);

        if (!snapshotCache.TryGetValue(baseCurrency, out var snapshot))
        {
            snapshot = await _provider.GetLatestRatesAsync(baseCurrency, cancellationToken);
            // Cached even when null, so an unreachable upstream is hit once per sweep
            // rather than once per user.
            snapshotCache[baseCurrency] = snapshot;
        }

        var now = DateTime.UtcNow;
        var unsupported = new List<string>();
        var updatedCount = 0;

        foreach (var currency in currencies)
        {
            var code = currency.Code?.Trim() ?? string.Empty;

            // The base currency is 1 against itself, always — a truth that holds with
            // or without the network, so it is repaired even on a failed fetch.
            if (string.Equals(code, baseCurrency, StringComparison.OrdinalIgnoreCase))
            {
                if (currency.ExchangeRateToBase != 1m)
                {
                    currency.ExchangeRateToBase = 1m;
                    currency.RateUpdatedAtUtc = now;
                    updatedCount++;
                }
                else if (snapshot is not null)
                {
                    // Unchanged but freshly confirmed: keep its age honest so the base
                    // row doesn't drag the "rates from N days ago" hint backwards.
                    currency.RateUpdatedAtUtc = now;
                }
                continue;
            }

            if (snapshot is null)
                continue;

            if (!snapshot.RatesToBase.TryGetValue(code, out var rate))
            {
                // The ECB publishes ~30 currencies; anything else (ALL, RSD, crypto)
                // keeps whatever the user typed in by hand.
                unsupported.Add(code);
                continue;
            }

            // Belt and braces over the provider's own guard: a non-positive rate makes
            // every converted total wrong in a way that is hard to notice.
            if (rate <= 0m)
            {
                unsupported.Add(code);
                continue;
            }

            var changed = currency.ExchangeRateToBase != rate;
            currency.ExchangeRateToBase = rate;
            // Stamped on every successful fetch, not only when the number moved: a rate
            // that genuinely held steady overnight is still freshly confirmed, and
            // showing it as days old would be a lie.
            currency.RateUpdatedAtUtc = now;
            if (changed)
                updatedCount++;
        }

        await _context.SaveChangesAsync(cancellationToken);

        if (snapshot is not null)
        {
            _logger.LogInformation(
                "Refreshed exchange rates for user {UserId} against {BaseCurrency} ({Changed} changed, {Unsupported} unsupported).",
                userId, baseCurrency, updatedCount, unsupported.Count);
        }

        return new ExchangeRateUpdateResult(
            baseCurrency,
            snapshot?.AsOf,
            updatedCount,
            unsupported,
            snapshot is null);
    }
}
