using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Application.Options;
using Microsoft.Extensions.Options;

namespace ExpenseTracker.Infrastructure.Jobs;

/// <summary>
/// Sugg-8's daily rate sweep, now as a scheduler job rather than a hosted service
/// of its own.
/// <para>
/// Nothing about the work changed — it still delegates to
/// <see cref="IExchangeRateUpdater"/> and still honours
/// <c>ExchangeRates:Enabled</c>. What changed is that it no longer keeps its own
/// timer: the old <c>ExchangeRateRefreshService</c> refreshed on startup and then
/// every 24 hours of <em>process uptime</em>, so a container that restarted daily
/// fetched daily and one that ran for a week fetched once. Measuring from a
/// persisted last-success instead makes the interval mean what it says.
/// </para>
/// </summary>
public sealed class ExchangeRateSweepJob : IScheduledJob
{
    private readonly IExchangeRateUpdater _updater;
    private readonly ExchangeRateOptions _options;

    public ExchangeRateSweepJob(IExchangeRateUpdater updater, IOptions<ExchangeRateOptions> options)
    {
        _updater = updater;
        _options = options.Value;
    }

    public string Name => ScheduledJobNames.ExchangeRates;

    public TimeSpan Interval => _options.Interval;

    public bool Enabled => _options.Enabled;

    public async Task<string> RunAsync(ScheduledJobContext context, CancellationToken cancellationToken = default)
    {
        var changed = await _updater.UpdateAllUsersAsync(cancellationToken);
        return $"{changed} rate(s) changed.";
    }
}
