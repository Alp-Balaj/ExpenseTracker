using ExpenseTracker.Application.DTOs;

namespace ExpenseTracker.Application.Interfaces;

/// <summary>How an action on the due queue ended, in terms the controller maps to a status code.</summary>
public enum PlannedActionStatus
{
    Success = 0,
    /// <summary>No such occurrence, or it belongs to someone else — 404 either way, so the two are never distinguishable.</summary>
    NotFound = 1,
    /// <summary>The request was understood and refused: already dealt with, no account named, amount not positive. 400.</summary>
    Invalid = 2
}

public readonly record struct PlannedActionResult(PlannedActionStatus Status, string ErrorMessage)
{
    public static PlannedActionResult Success() => new(PlannedActionStatus.Success, string.Empty);
    public static PlannedActionResult NotFound() => new(PlannedActionStatus.NotFound, string.Empty);
    public static PlannedActionResult Invalid(string message) => new(PlannedActionStatus.Invalid, message);
}

/// <summary>
/// The due queue behind planned transactions: filling it, and turning an entry
/// into real money.
/// </summary>
public interface IPlannedTransactionService
{
    /// <summary>
    /// Materialises every occurrence that has come due since the last call, for
    /// the current user.
    /// <para>
    /// Runs on read rather than on a timer. Nothing posts unattended, so an
    /// occurrence matters only when someone is looking at it — which makes
    /// catch-up after the app was down the ordinary code path rather than a
    /// special case, and removes the scheduler entirely.
    /// </para>
    /// <para>
    /// Idempotent, and safe to run concurrently: the unique
    /// (PlannedTransactionId, PeriodKey) index is what decides the race.
    /// </para>
    /// </summary>
    /// <returns>How many new occurrences were queued.</returns>
    Task<int> GenerateDueOccurrencesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Turns a pending occurrence into a real Expense, Income or Transfer,
    /// moving the account balances through the same path the controllers use,
    /// inside one database transaction with the state change.
    /// </summary>
    Task<PlannedActionResult> CommitAsync(
        Guid occurrenceId, CommitPlannedOccurrenceDTO commit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a pending occurrence as didn't-happen. Posts nothing and touches no
    /// balance. On a one-off template this is effectively a cancellation.
    /// </summary>
    Task<PlannedActionResult> SkipAsync(Guid occurrenceId, CancellationToken cancellationToken = default);
}
