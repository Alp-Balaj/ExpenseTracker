using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Application.Options;
using Microsoft.Extensions.Options;

namespace ExpenseTracker.API.Services;

/// <summary>
/// Sugg-8's daily sweep: refresh every user's exchange rates on startup and then once
/// every <c>ExchangeRates:IntervalHours</c>. Hand-maintained rates go stale the day
/// they're typed, and since Fix-10 they are actually read, so the drift shows up in
/// every converted total.
///
/// Lives in the API project because a hosted service is a host concern, not a data or
/// domain one (Sugg-11's layering point). All it does is open a scope and delegate —
/// <see cref="IExchangeRateUpdater"/> is scoped, this service is a singleton.
/// </summary>
public sealed class ExchangeRateRefreshService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ExchangeRateOptions _options;
    private readonly ILogger<ExchangeRateRefreshService> _logger;

    public ExchangeRateRefreshService(
        IServiceScopeFactory scopeFactory,
        IOptions<ExchangeRateOptions> options,
        ILogger<ExchangeRateRefreshService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            // The test suite sets this false so no test ever depends on the network.
            _logger.LogInformation("Automatic exchange rate refresh is disabled (ExchangeRates:Enabled=false).");
            return;
        }

        try
        {
            // Program.cs runs db.Database.Migrate() before app.Run(), so the schema is
            // already in place when this starts; the delay only keeps the first fetch
            // off the critical path of a cold start.
            await Task.Delay(_options.StartupDelay, stoppingToken);

            using var timer = new PeriodicTimer(_options.Interval);
            do
            {
                await RefreshAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private async Task RefreshAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var updater = scope.ServiceProvider.GetRequiredService<IExchangeRateUpdater>();
            var changed = await updater.UpdateAllUsersAsync(stoppingToken);
            _logger.LogInformation("Exchange rate sweep complete: {Changed} rate(s) changed.", changed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed tick must never take the service down — that would silently stop
            // every future refresh, which is exactly the stale-rate problem this fixes.
            _logger.LogError(ex, "Exchange rate sweep failed; retrying on the next tick.");
        }
    }
}
