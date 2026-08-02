namespace ExpenseTracker.Application.Interfaces
{
    /// <summary>The stable names background jobs are keyed by in the database.</summary>
    /// <remarks>
    /// Constants rather than class names: renaming a job type would otherwise
    /// silently orphan its last-run row, and the very next tick would re-run a
    /// job that had been up to date for months.
    /// </remarks>
    public static class ScheduledJobNames
    {
        public const string ExchangeRates = "exchange-rates";
        public const string PlannedOccurrences = "planned-occurrences";
        public const string PlannedDueReminders = "planned-due-reminders";
        public const string BudgetThresholds = "budget-thresholds";
        public const string WeeklyDigest = "weekly-digest";
    }

    /// <summary>
    /// What a job is told when it runs.
    /// </summary>
    /// <param name="NowUtc">
    /// The instant the run was claimed. Passed in rather than read from the clock
    /// so a job's own arithmetic is testable and so every job in one tick agrees
    /// on what "now" is.
    /// </param>
    /// <param name="LastSucceededAtUtc">
    /// When this job last completed without throwing, or null if it never has.
    /// This is the catch-up handle: a job that covers a window (the weekly
    /// digest, a reminder sweep) reconciles from here instead of assuming the
    /// previous tick fired.
    /// </param>
    public readonly record struct ScheduledJobContext(DateTime NowUtc, DateTime? LastSucceededAtUtc);

    /// <summary>
    /// One unit of unattended work.
    /// <para>
    /// The contract is deliberately narrow so <see cref="Interval"/> is the only
    /// scheduling concept in the system — there is no cron expression, no "run at
    /// 03:00", nothing that assumes the process was alive at a particular moment.
    /// Railway runs a single container that can sleep or be redeployed mid-tick,
    /// so the only schedule that survives is "it has been long enough since the
    /// last success".
    /// </para>
    /// <para>
    /// Implementations must be idempotent and safe to run late, twice, or after a
    /// three-day gap. Stream M's demo-expiry job is expected to slot in here with
    /// no change to the host.
    /// </para>
    /// </summary>
    public interface IScheduledJob
    {
        /// <summary>Key into <c>ScheduledJobRun</c> — one of <see cref="ScheduledJobNames"/>.</summary>
        string Name { get; }

        /// <summary>Minimum time between successful runs.</summary>
        TimeSpan Interval { get; }

        /// <summary>
        /// False when the job's own configuration turns it off (the rate sweep
        /// respects <c>ExchangeRates:Enabled</c>, the notification jobs go quiet
        /// when no VAPID keys are configured). A disabled job is skipped without
        /// claiming, so it leaves no last-run row and starts clean when enabled.
        /// </summary>
        bool Enabled { get; }

        /// <summary>Runs the job and returns a one-line summary for the log and the run row.</summary>
        Task<string> RunAsync(ScheduledJobContext context, CancellationToken cancellationToken = default);
    }
}
