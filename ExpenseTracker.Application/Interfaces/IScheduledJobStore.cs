namespace ExpenseTracker.Application.Interfaces
{
    /// <summary>A won claim on a job, carrying the history the job catches up from.</summary>
    public readonly record struct ScheduledJobClaim(string JobName, DateTime? LastSucceededAtUtc);

    /// <summary>
    /// The persisted half of the scheduler: when each job last ran, and who is
    /// running it right now.
    /// </summary>
    public interface IScheduledJobStore
    {
        /// <summary>
        /// Claims <paramref name="jobName"/> if it is due — that is, if it has
        /// never succeeded, or the last attempt was more than
        /// <paramref name="interval"/> ago — and if nobody else holds it.
        /// <para>
        /// The whole decision is one conditional UPDATE, so the database, not the
        /// process, is what arbitrates. Railway runs a single container today, but
        /// a deploy briefly overlaps the old one with the new, and that window is
        /// exactly when a double-send would happen.
        /// </para>
        /// </summary>
        /// <returns>The claim if this caller won it; null if the job is not due or is already claimed.</returns>
        Task<ScheduledJobClaim?> TryClaimAsync(
            string jobName, TimeSpan interval, DateTime nowUtc, CancellationToken cancellationToken = default);

        /// <summary>
        /// Releases a claim and records the outcome. A failure advances
        /// <c>LastRunAtUtc</c> (so a permanently broken job retries on its
        /// interval rather than on every tick) but never
        /// <c>LastSucceededAtUtc</c> — the window it failed to cover stays
        /// uncovered, and the next successful run catches it up.
        /// </summary>
        Task CompleteAsync(
            string jobName, DateTime completedAtUtc, bool succeeded, string result,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Frees claims older than <paramref name="staleAfter"/>. A container
        /// killed mid-job leaves its claim behind for ever otherwise, and the job
        /// would never run again — the one failure mode a claim can introduce.
        /// </summary>
        Task<int> ReleaseStaleClaimsAsync(
            TimeSpan staleAfter, DateTime nowUtc, CancellationToken cancellationToken = default);
    }
}
