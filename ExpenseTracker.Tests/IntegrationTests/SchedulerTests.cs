using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ExpenseTracker.Tests.IntegrationTests;

// Stream P's scheduler, driven through IScheduledJobStore rather than through the
// hosted service — the same reasoning as ExchangeRateRefreshTests driving the
// refresh endpoint instead of the timer. The host is a PeriodicTimer around this;
// everything that can actually be wrong lives here.
//
// The property under test throughout is catch-up: being due is a question about a
// persisted last-success timestamp, never about whether a tick fired. Railway's
// container is free to sleep, and a scheduler that assumes otherwise loses jobs
// silently.
[Collection(IntegrationTestCollection.Name)]
public sealed class SchedulerTests
{
    private readonly CustomWebApplicationFactory _factory;

    public SchedulerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // A fresh name per test: the store is keyed by job name and the suite shares
    // one database, so a real job name would make these tests each other's state.
    private static string NewJobName() => $"test-{Guid.NewGuid():N}";

    private static readonly TimeSpan Daily = TimeSpan.FromHours(24);

    private async Task<T> WithStoreAsync<T>(Func<IScheduledJobStore, Task<T>> action)
    {
        // A scope per call, because that is how the host uses it: one scope per
        // job, so no job can poison another's DbContext.
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IScheduledJobStore>());
    }

    private Task WithStoreAsync(Func<IScheduledJobStore, Task> action) =>
        WithStoreAsync<object?>(async store => { await action(store); return null; });

    [Fact]
    public async Task A_job_that_has_never_run_is_due_at_once()
    {
        var job = NewJobName();
        var now = DateTime.UtcNow;

        var claim = await WithStoreAsync(store => store.TryClaimAsync(job, Daily, now));

        Assert.NotNull(claim);
        Assert.Equal(job, claim!.Value.JobName);
        // Nothing to catch up from yet — a first run covers whatever it covers.
        Assert.Null(claim.Value.LastSucceededAtUtc);
    }

    [Fact]
    public async Task A_held_claim_cannot_be_taken_twice()
    {
        var job = NewJobName();
        var now = DateTime.UtcNow;

        Assert.NotNull(await WithStoreAsync(store => store.TryClaimAsync(job, Daily, now)));

        // The window that matters: a Railway deploy briefly runs the old container
        // alongside the new one, and both tick. Only one may send the notification.
        Assert.Null(await WithStoreAsync(store => store.TryClaimAsync(job, Daily, now)));
    }

    [Fact]
    public async Task A_completed_job_is_not_due_again_until_its_interval_has_passed()
    {
        var job = NewJobName();
        var completedAt = DateTime.UtcNow;

        Assert.NotNull(await WithStoreAsync(store => store.TryClaimAsync(job, Daily, completedAt)));
        await WithStoreAsync(store => store.CompleteAsync(job, completedAt, succeeded: true, "done"));

        // Ticks every fifteen minutes all day: none of them may run it again.
        Assert.Null(await WithStoreAsync(store => store.TryClaimAsync(job, Daily, completedAt.AddHours(1))));
        Assert.Null(await WithStoreAsync(store => store.TryClaimAsync(job, Daily, completedAt.AddHours(23))));

        var next = await WithStoreAsync(store => store.TryClaimAsync(job, Daily, completedAt.AddHours(24)));
        Assert.NotNull(next);
        Assert.Equal(completedAt, next!.Value.LastSucceededAtUtc!.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task A_container_that_slept_for_three_days_catches_up_exactly_once()
    {
        var job = NewJobName();
        var beforeTheOutage = DateTime.UtcNow.AddDays(-3);

        Assert.NotNull(await WithStoreAsync(store => store.TryClaimAsync(job, Daily, beforeTheOutage)));
        await WithStoreAsync(store => store.CompleteAsync(job, beforeTheOutage, succeeded: true, "done"));

        var wakeUp = DateTime.UtcNow;

        var caughtUp = await WithStoreAsync(store => store.TryClaimAsync(job, Daily, wakeUp));
        Assert.NotNull(caughtUp);
        // And it knows how far back the gap goes, so a job that covers a window can
        // reconcile it rather than assume yesterday.
        Assert.Equal(beforeTheOutage, caughtUp!.Value.LastSucceededAtUtc!.Value, TimeSpan.FromMilliseconds(1));

        await WithStoreAsync(store => store.CompleteAsync(job, wakeUp, succeeded: true, "caught up"));

        // Three missed days produce one run, not three.
        Assert.Null(await WithStoreAsync(store => store.TryClaimAsync(job, Daily, wakeUp.AddMinutes(15))));
    }

    [Fact]
    public async Task A_failed_run_backs_off_to_its_interval_but_leaves_the_window_uncovered()
    {
        var job = NewJobName();
        var failedAt = DateTime.UtcNow;

        Assert.NotNull(await WithStoreAsync(store => store.TryClaimAsync(job, Daily, failedAt)));
        await WithStoreAsync(store => store.CompleteAsync(job, failedAt, succeeded: false, "upstream unreachable"));

        // Backed off: a permanently broken job must not retry on every tick.
        Assert.Null(await WithStoreAsync(store => store.TryClaimAsync(job, Daily, failedAt.AddHours(1))));

        var retry = await WithStoreAsync(store => store.TryClaimAsync(job, Daily, failedAt.AddHours(24)));
        Assert.NotNull(retry);
        // But the failure did not count as covering anything — the next successful
        // run still catches up from before it.
        Assert.Null(retry!.Value.LastSucceededAtUtc);
    }

    [Fact]
    public async Task A_claim_left_behind_by_a_killed_container_is_released_rather_than_stranding_the_job()
    {
        var job = NewJobName();
        var died = DateTime.UtcNow.AddHours(-2);

        Assert.NotNull(await WithStoreAsync(store => store.TryClaimAsync(job, Daily, died)));
        // ... and the container is killed here, so CompleteAsync never runs.

        var now = DateTime.UtcNow;
        Assert.Null(await WithStoreAsync(store => store.TryClaimAsync(job, Daily, now)));

        var released = await WithStoreAsync(store =>
            store.ReleaseStaleClaimsAsync(TimeSpan.FromMinutes(30), now));
        Assert.True(released >= 1);

        // Without this sweep the job would never run again — the one failure mode
        // claiming introduces.
        Assert.NotNull(await WithStoreAsync(store => store.TryClaimAsync(job, Daily, now)));
    }

    [Fact]
    public async Task A_claim_still_inside_the_stale_window_is_left_alone()
    {
        var job = NewJobName();
        var now = DateTime.UtcNow;

        Assert.NotNull(await WithStoreAsync(store => store.TryClaimAsync(job, Daily, now)));

        await WithStoreAsync(store => store.ReleaseStaleClaimsAsync(TimeSpan.FromMinutes(30), now.AddMinutes(5)));

        // A slow job is not a dead one; releasing it would be the double-run this
        // whole mechanism exists to prevent.
        Assert.Null(await WithStoreAsync(store => store.TryClaimAsync(job, Daily, now.AddMinutes(5))));
    }

    [Fact]
    public async Task Every_registered_job_has_a_distinct_name_and_a_sane_interval()
    {
        using var scope = _factory.Services.CreateScope();
        var jobs = scope.ServiceProvider.GetServices<IScheduledJob>().ToList();

        // Names are the primary key of the run table: two jobs sharing one would
        // silently mean one schedule for both.
        Assert.Equal(jobs.Select(j => j.Name).Distinct().Count(), jobs.Count);
        Assert.All(jobs, job => Assert.True(job.Interval >= TimeSpan.FromMinutes(1)));

        Assert.Contains(ScheduledJobNames.ExchangeRates, jobs.Select(j => j.Name));
        Assert.Contains(ScheduledJobNames.PlannedOccurrences, jobs.Select(j => j.Name));
        Assert.Contains(ScheduledJobNames.PlannedDueReminders, jobs.Select(j => j.Name));
        Assert.Contains(ScheduledJobNames.BudgetThresholds, jobs.Select(j => j.Name));
        Assert.Contains(ScheduledJobNames.WeeklyDigest, jobs.Select(j => j.Name));
    }
}
