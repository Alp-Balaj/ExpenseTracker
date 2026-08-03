namespace ExpenseTracker.Application.Interfaces;

/// <summary>
/// Stream V's one source of truth for reserved money. A savings goal is
/// virtual, but the money behind it is not: every contribution names the
/// account it is held against, so "usable" is an account's balance minus what
/// its contributions have spoken for.
/// </summary>
public interface ISavingsReservationService
{
    /// <summary>
    /// Reserved money per account for the current user, keyed by account id.
    /// Accounts with nothing reserved are absent rather than zero. One grouped
    /// query — callers must not sum contributions themselves.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, decimal>> GetReservedByAccountAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The currency a goal is denominated in: that of the account its earliest
    /// contribution was made from. Null when the goal has no contributions, in
    /// which case any account may fund it and the first one to do so fixes the
    /// answer.
    /// </summary>
    Task<string?> GetGoalCurrencyCodeAsync(Guid goalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that <paramref name="accountId"/> may hold money for
    /// <paramref name="goalId"/>: the account must exist and belong to the
    /// user, and must match the goal's established currency.
    /// </summary>
    /// <param name="ignoreContributionId">
    /// A contribution being edited or deleted, excluded when working out the
    /// goal's currency so a goal's only contribution can always be corrected.
    /// </param>
    /// <returns>An error message, or null when the pairing is legal.</returns>
    Task<string?> ValidateContributionAccountAsync(
        Guid goalId,
        Guid? accountId,
        Guid? ignoreContributionId = null,
        CancellationToken cancellationToken = default);
}
