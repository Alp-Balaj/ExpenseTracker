using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Application.Options;
using Microsoft.Extensions.Options;

namespace ExpenseTracker.API.Services;

/// <summary>
/// Stream P's background scheduler: wakes up every <c>Scheduler:TickMinutes</c>,
/// asks each registered <see cref="IScheduledJob"/> whether it is due, and runs
/// the ones that are.
///
/// <para>
/// <strong>Catch-up by design.</strong> Railway runs a single container that is
/// allowed to sleep, and every deploy replaces it. So this host deliberately has
/// no notion of "fires at 03:00": a tick nobody was awake for is not a late job,
/// it is a lost one. Instead each job carries an interval, the database remembers
/// when it last succeeded, and being due is a question answered from those two —
/// which makes a container that was down for three days indistinguishable from
/// one that merely woke up late. It runs once, immediately, and reconciles.
/// </para>
///
/// <para>
/// The frequent tick is not the schedule. A daily job checked every fifteen
/// minutes still runs daily; the tick only bounds how long after waking the
/// container notices.
/// </para>
///
/// <para>
/// Lives in the API project because hosting is a host concern (Sugg-11's layering
/// point), exactly like <c>ExchangeRateRefreshService</c> before it — this
/// host absorbed that one, so the rate sweep is now
/// <c>ScheduledJobNames.ExchangeRates</c> and there is a single mechanism for
/// unattended work instead of two.
/// </para>
/// </summary>
public sealed class SchedulerHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SchedulerOptions _options;
    private readonly ILogger<SchedulerHostedService> _logger;

    public SchedulerHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<SchedulerOptions> options,
        ILogger<SchedulerHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            // The test suite sets this false: integration tests drive the jobs
            // directly so they stay deterministic, and no test may wait on a timer.
            _logger.LogInformation("Background scheduler is disabled (Scheduler:Enabled=false).");
            return;
        }

        try
        {
            // Program.cs migrates before app.Run(), so the schema is in place by
            // the time this starts; the delay only keeps the first sweep off the
            // critical path of a cold start.
            await Task.Delay(_options.StartupDelay, stoppingToken);

            using var timer = new PeriodicTimer(_options.Tick);
            do
            {
                await TickAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private async Task TickAsync(CancellationToken stoppingToken)
    {
        var nowUtc = DateTime.UtcNow;

        try
        {
            List<(string Name, TimeSpan Interval)> due;

            using (var scope = _scopeFactory.CreateScope())
            {
                var store = scope.ServiceProvider.GetRequiredService<IScheduledJobStore>();

                // A container killed mid-job leaves its claim behind, and a claim
                // nobody will ever release is a job that never runs again. This is
                // the only thing that makes claiming safe rather than a new way to
                // break.
                var released = await store.ReleaseStaleClaimsAsync(_options.StaleClaim, nowUtc, stoppingToken);
                if (released > 0)
                    _logger.LogWarning("Released {Count} stale scheduler claim(s).", released);

                due = scope.ServiceProvider.GetServices<IScheduledJob>()
                    .Where(job => job.Enabled)
                    .Select(job => (job.Name, job.Interval))
                    .ToList();
            }

            foreach (var (name, interval) in due)
                await RunIfDueAsync(name, interval, nowUtc, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed tick must never take the host down — that would silently
            // stop every future job, which is a worse outage than any one job's.
            _logger.LogError(ex, "Scheduler tick failed; retrying on the next tick.");
        }
    }

    /// <summary>
    /// One job, one scope. A job that leaves its DbContext in a bad state (a
    /// failed insert, a poisoned change tracker) must not take the rest of the
    /// tick down with it.
    /// </summary>
    private async Task RunIfDueAsync(string name, TimeSpan interval, DateTime nowUtc, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IScheduledJobStore>();

        var claim = await store.TryClaimAsync(name, interval, nowUtc, stoppingToken);
        if (claim is null) return;

        var job = scope.ServiceProvider.GetServices<IScheduledJob>().FirstOrDefault(j => j.Name == name);
        if (job is null)
        {
            await store.CompleteAsync(name, DateTime.UtcNow, false, "Job no longer registered.", CancellationToken.None);
            return;
        }

        try
        {
            var summary = await job.RunAsync(new ScheduledJobContext(nowUtc, claim.Value.LastSucceededAtUtc), stoppingToken);
            await store.CompleteAsync(name, DateTime.UtcNow, true, summary, CancellationToken.None);
            _logger.LogInformation("Scheduled job {Job} completed: {Summary}", name, summary);
        }
        catch (Exception ex)
        {
            // CancellationToken.None on purpose: shutdown cancelled the job, but
            // the claim still has to be released or the job is stuck until the
            // stale sweep notices half an hour later.
            await store.CompleteAsync(name, DateTime.UtcNow, false, ex.Message, CancellationToken.None);

            if (ex is OperationCanceledException) throw;
            _logger.LogError(ex, "Scheduled job {Job} failed; it will retry on its interval.", name);
        }
    }
}
