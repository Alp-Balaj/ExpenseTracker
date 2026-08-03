using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Infrastructure.Services;

/// <inheritdoc cref="IScheduledJobStore"/>
/// <remarks>
/// Every decision here is a single SQL statement rather than read-then-write.
/// The scheduler's one dangerous failure mode is running a job twice — a
/// duplicate push, or a duplicate demo purge once Stream M lands — and the only
/// arbiter that survives a container being replaced mid-tick is the database.
/// </remarks>
public sealed class ScheduledJobStore : IScheduledJobStore
{
    private const int MaxResultLength = 500;

    private readonly ApplicationDbContext _context;

    public ScheduledJobStore(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ScheduledJobClaim?> TryClaimAsync(
        string jobName, TimeSpan interval, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await EnsureRowExistsAsync(jobName, cancellationToken);

        // "Due" is measured from the last *attempt*, not the last success: a job
        // that throws every time must back off to its interval rather than retry
        // on every tick. What it catches up *from* is the last success, which is
        // handed to the job below and left untouched by a failure.
        var dueBefore = nowUtc - interval;

        var claimed = await _context.ScheduledJobRuns
            .Where(j => j.JobName == jobName
                        && j.ClaimedAtUtc == null
                        && (j.LastRunAtUtc == null || j.LastRunAtUtc <= dueBefore))
            .ExecuteUpdateAsync(
                set => set.SetProperty(j => j.ClaimedAtUtc, nowUtc),
                cancellationToken);

        if (claimed == 0) return null;

        // Safe to read after the claim: nothing but this caller may write the row
        // while the claim is held, and the claim did not touch these columns.
        var lastSucceeded = await _context.ScheduledJobRuns
            .Where(j => j.JobName == jobName)
            .Select(j => j.LastSucceededAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return new ScheduledJobClaim(jobName, lastSucceeded);
    }

    public async Task CompleteAsync(
        string jobName, DateTime completedAtUtc, bool succeeded, string result,
        CancellationToken cancellationToken = default)
    {
        var trimmed = string.IsNullOrEmpty(result)
            ? null
            : result.Length <= MaxResultLength ? result : result[..MaxResultLength];

        var rows = _context.ScheduledJobRuns.Where(j => j.JobName == jobName);

        if (succeeded)
        {
            await rows.ExecuteUpdateAsync(
                set => set
                    .SetProperty(j => j.ClaimedAtUtc, (DateTime?)null)
                    .SetProperty(j => j.LastRunAtUtc, completedAtUtc)
                    .SetProperty(j => j.LastSucceededAtUtc, completedAtUtc)
                    .SetProperty(j => j.LastResult, trimmed)
                    .SetProperty(j => j.ConsecutiveFailures, 0),
                cancellationToken);
        }
        else
        {
            // LastSucceededAtUtc deliberately untouched — the window this run
            // failed to cover is still uncovered, and the next success has to
            // reconcile it.
            await rows.ExecuteUpdateAsync(
                set => set
                    .SetProperty(j => j.ClaimedAtUtc, (DateTime?)null)
                    .SetProperty(j => j.LastRunAtUtc, completedAtUtc)
                    .SetProperty(j => j.LastResult, trimmed)
                    .SetProperty(j => j.ConsecutiveFailures, j => j.ConsecutiveFailures + 1),
                cancellationToken);
        }
    }

    public Task<int> ReleaseStaleClaimsAsync(
        TimeSpan staleAfter, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var threshold = nowUtc - staleAfter;

        return _context.ScheduledJobRuns
            .Where(j => j.ClaimedAtUtc != null && j.ClaimedAtUtc < threshold)
            .ExecuteUpdateAsync(
                set => set.SetProperty(j => j.ClaimedAtUtc, (DateTime?)null),
                cancellationToken);
    }

    /// <summary>
    /// First tick after a deploy finds no row for a new job. Insert it losing the
    /// race is fine and expected — the primary key is the arbiter, and the loser
    /// simply proceeds to the claim like everyone else.
    /// </summary>
    private async Task EnsureRowExistsAsync(string jobName, CancellationToken cancellationToken)
    {
        var exists = await _context.ScheduledJobRuns
            .AnyAsync(j => j.JobName == jobName, cancellationToken);

        if (exists) return;

        _context.ScheduledJobRuns.Add(new ScheduledJobRun { JobName = jobName });

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Someone else inserted it between the check and the write. Detach so
            // the failed insert cannot be replayed by a later SaveChanges on this
            // same scoped context.
            foreach (var entry in _context.ChangeTracker.Entries<ScheduledJobRun>().ToList())
                entry.State = EntityState.Detached;
        }
    }
}
