namespace ExpenseTracker.Domain.Entities
{
    /// <summary>
    /// One row per background job, remembering when it last ran.
    /// <para>
    /// This row <em>is</em> the scheduler. Railway runs a single container that is
    /// free to sleep, restart or redeploy at any moment, so a job must never be
    /// defined as "fires at 03:00" — a tick that nobody was awake for is simply
    /// lost. Instead every job asks the same question on every wake-up: has it
    /// been longer than my interval since <see cref="LastSucceededAtUtc"/>? A
    /// container that was down for three days answers yes once and catches up,
    /// rather than firing three times or not at all.
    /// </para>
    /// <para>
    /// Deliberately not a <c>BaseEntity</c>: this belongs to the deployment, not
    /// to a user, and giving it a UserId would put it under the cascade rule that
    /// <c>ApplicationDbContext.ConfigureUserOwnership</c> applies to everything
    /// a user owns.
    /// </para>
    /// </summary>
    public class ScheduledJobRun
    {
        /// <summary>
        /// The job's stable identifier and the primary key — see
        /// <c>ScheduledJobNames</c>. Renaming a job resets its history, which is
        /// why the names are constants rather than typeof(...).Name.
        /// </summary>
        public required string JobName { get; set; }

        /// <summary>
        /// When the currently running attempt started, cleared when it finishes.
        /// Non-null means "someone is on it": the claim is a conditional UPDATE
        /// on this column, so two containers (or a duplicated deploy overlapping
        /// with the old one) cannot both run the same job.
        /// </summary>
        public DateTime? ClaimedAtUtc { get; set; }

        /// <summary>Last attempt, successful or not — what the claim measures against.</summary>
        public DateTime? LastRunAtUtc { get; set; }

        /// <summary>
        /// Last attempt that completed without throwing. This, not
        /// <see cref="LastRunAtUtc"/>, is what a job catches up <em>from</em>:
        /// a failed sweep must not be treated as covering its window.
        /// </summary>
        public DateTime? LastSucceededAtUtc { get; set; }

        /// <summary>What the last run reported, or the exception message if it failed.</summary>
        public string? LastResult { get; set; }

        /// <summary>Consecutive failures; reset by any success. Purely diagnostic.</summary>
        public int ConsecutiveFailures { get; set; }
    }
}
