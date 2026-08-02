namespace ExpenseTracker.Application.Options
{
    /// <summary>
    /// Binds the "Scheduler" configuration section — the host's ticking, not any
    /// individual job's schedule (that is <c>IScheduledJob.Interval</c>).
    /// </summary>
    public sealed class SchedulerOptions
    {
        public const string SectionName = "Scheduler";

        /// <summary>
        /// Turns the whole background host off. The test suite sets this false:
        /// integration tests drive the jobs directly so they stay deterministic,
        /// and no test may depend on a timer.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// How often the host wakes up and asks every job whether it is due. Not
        /// the same thing as how often jobs run — a daily job checked every
        /// fifteen minutes still runs daily, but a container that was asleep at
        /// midnight catches up within fifteen minutes of waking rather than
        /// waiting for the next midnight that may never come while it is awake.
        /// </summary>
        public double TickMinutes { get; set; } = 15;

        /// <summary>Keeps the first sweep off the critical path of a cold start.</summary>
        public double StartupDelaySeconds { get; set; } = 20;

        /// <summary>
        /// How long a claim may be held before it is assumed to belong to a
        /// container that died mid-job. Generous: the planned-occurrence sweep
        /// walks every user, and re-running a job because it was merely slow is
        /// worse than waiting.
        /// </summary>
        public double StaleClaimMinutes { get; set; } = 30;

        // Clamped so a typo can neither turn the host into a hot loop nor push a
        // daily job a month out.
        public TimeSpan Tick => TimeSpan.FromMinutes(Math.Clamp(TickMinutes, 1, 24 * 60));
        public TimeSpan StartupDelay => TimeSpan.FromSeconds(Math.Clamp(StartupDelaySeconds, 0, 3600));
        public TimeSpan StaleClaim => TimeSpan.FromMinutes(Math.Clamp(StaleClaimMinutes, 1, 24 * 60));
    }
}
