using ExpenseTracker.Application.Interfaces;

namespace ExpenseTracker.Infrastructure.Jobs;

/// <summary>
/// Materialises every planned occurrence that has come due, for every user.
/// <para>
/// Sugg-4 deliberately generated on read and called that "removing the scheduler
/// entirely" — which was right for correctness and is still what the queue relies
/// on. It stops being sufficient the moment something other than a person needs
/// the queue to be current: the due-tomorrow reminder runs at four in the morning
/// with nobody looking, and a queue that only fills itself when the app is opened
/// would have it reminding about bills a week after they were paid.
/// </para>
/// <para>
/// Nothing here posts money. Generation only ever queues Pending occurrences for
/// a human to commit or skip, exactly as before.
/// </para>
/// </summary>
public sealed class PlannedOccurrenceSweepJob : IScheduledJob
{
    private readonly IPlannedTransactionService _planned;

    public PlannedOccurrenceSweepJob(IPlannedTransactionService planned)
    {
        _planned = planned;
    }

    public string Name => ScheduledJobNames.PlannedOccurrences;

    public TimeSpan Interval => TimeSpan.FromHours(24);

    public bool Enabled => true;

    public async Task<string> RunAsync(ScheduledJobContext context, CancellationToken cancellationToken = default)
    {
        var queued = await _planned.GenerateDueOccurrencesForAllUsersAsync(cancellationToken);
        return $"{queued} occurrence(s) queued.";
    }
}
