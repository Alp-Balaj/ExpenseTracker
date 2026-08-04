using ExpenseTracker.Application.DTOs;

namespace ExpenseTracker.Application.Interfaces
{
    /// <summary>
    /// Cycle U W2: money in, money out, and whose it was.
    ///
    /// <para>
    /// The whole service exists to keep one sentence true after every single
    /// operation:
    /// </para>
    /// <code>
    /// Bᵢ = topups + outOfPocket − shares − withdrawals
    /// Σ Bᵢ ≡ pot cash balance
    /// </code>
    /// <para>
    /// Nothing here stores a balance. The pot's cash and every member's position
    /// are summed from the rows on each read, which is why no concurrency token
    /// is needed and why there is nothing that can drift: the invariant is not
    /// maintained, it is a consequence of the arithmetic. Two members spending
    /// at once produce two rows and one correct total.
    /// </para>
    ///
    /// <para>
    /// The same null-means-404 contract as <see cref="IPotService"/>: a
    /// non-member is answered exactly as somebody asking about a pot that never
    /// existed. Where a call fails for a reason worth explaining — a withdrawal
    /// over the cap, shares that do not add up — it returns the sentence and the
    /// controller answers 400.
    /// </para>
    /// </summary>
    public interface IPotLedgerService
    {
        /// <summary>
        /// The pot's money screen: balance, every member's position, and the
        /// feed. Null when the caller is not a member.
        /// </summary>
        Task<PotLedgerDTO?> GetLedgerAsync(Guid potId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Moves money from one of the caller's own accounts into the pot. A
        /// transfer: it debits the account and touches no budget, because the
        /// money is not consumed yet.
        /// </summary>
        Task<(PotLedgerDTO? Ledger, string? Error)> TopUpAsync(
            Guid potId, CreatePotTopUpDTO dto, CancellationToken cancellationToken = default);

        /// <summary>
        /// Records something the group spent, and who it was for. Writes the
        /// expense and its N shares in one transaction, allocated by largest
        /// remainder so <c>Σ shares ≡ amount</c> exactly.
        /// </summary>
        Task<(PotLedgerDTO? Ledger, string? Error)> LogExpenseAsync(
            Guid potId, CreatePotExpenseDTO dto, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reverses an expense completely: the pot's debit — or the payer's
        /// account, if they fronted it — and every share, in one transaction.
        /// </summary>
        Task<(PotLedgerDTO? Ledger, string? Error)> DeleteExpenseAsync(
            Guid potId, Guid expenseId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Takes the caller's own unspent stake out to an account they own,
        /// capped at their positive position.
        /// </summary>
        Task<(PotLedgerDTO? Ledger, string? Error)> WithdrawAsync(
            Guid potId, CreatePotWithdrawalDTO dto, CancellationToken cancellationToken = default);
    }
}
