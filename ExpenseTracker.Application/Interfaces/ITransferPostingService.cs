using ExpenseTracker.Domain.Entities;

namespace ExpenseTracker.Application.Interfaces;

/// <summary>
/// The mechanics of booking a transfer: moving both balances and keeping the
/// optional savings contribution in step.
///
/// Extracted from <c>TransferController</c> so committing a planned transfer
/// (Sugg-4) goes through exactly the same code, rather than a second
/// implementation that would drift from it. Nothing here opens a transaction or
/// saves — the caller owns both, because every one of these operations only
/// makes sense paired with a row change that has to commit or roll back with it.
/// </summary>
public interface ITransferPostingService
{
    /// <summary>
    /// Moves <paramref name="amount"/> between two accounts: sign +1 books the
    /// move, -1 reverses it. Both legs are user-scoped atomic UPDATEs (Fix-2's
    /// mechanism). Returns an error message, or null on success.
    /// </summary>
    Task<string?> ApplyMoveAsync(
        decimal amount, Guid fromAccountId, Guid toAccountId, int sign,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Books the savings contribution for a transfer that names a goal and
    /// stamps the transfer with its id. No goal named: nothing to do.
    /// </summary>
    Task<string?> LinkContributionAsync(Transfer transfer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reverses whatever <see cref="LinkContributionAsync"/> booked. A null id
    /// means there was never a link; a non-null id with no row behind it means
    /// the contribution was deleted on its own, which already corrected the
    /// goal total.
    /// </summary>
    Task<string?> UnlinkContributionAsync(Guid? savingContributionId, CancellationToken cancellationToken = default);
}
