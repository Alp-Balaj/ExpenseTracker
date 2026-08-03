using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Domain.Entities;

namespace ExpenseTracker.Application.Interfaces
{
    /// <summary>
    /// Stream R: everything that keeps a split bill's receivable true.
    /// <para>
    /// The read side serves the "owed to me" surface; the three expense hooks
    /// are called from inside <c>TransactionControllerBase</c>'s existing
    /// transaction, so a reimbursement can never be written without its expense
    /// or survive it.
    /// </para>
    /// </summary>
    public interface IReimbursementService
    {
        /// <param name="includeSettled">
        /// False (the default the endpoint uses) returns only receivables with
        /// something still out.
        /// </param>
        Task<IReadOnlyList<ReimbursementDTO>> GetAsync(bool includeSettled, CancellationToken cancellationToken = default);

        /// <summary>
        /// Books one instalment: credits the chosen account and records the
        /// settlement, in one transaction. No income row is written — a
        /// settlement is money coming back, not money earned.
        /// </summary>
        /// <returns>
        /// The refreshed receivable on success; <c>(null, message)</c> for a
        /// business-rule 400; <c>(null, null)</c> when the id is not the current
        /// user's, which the controller answers with 404.
        /// </returns>
        Task<(ReimbursementDTO? Reimbursement, string? Error)> SettleAsync(
            Guid reimbursementId, SettleReimbursementDTO dto, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates the receivable for a just-added expense. The expense is
        /// already in the change tracker; this adds to the same SaveAsync.
        /// </summary>
        Task<string?> ApplyToCreatedExpenseAsync(
            Expense expense, decimal? expectedReimbursement, CancellationToken cancellationToken = default);

        /// <summary>
        /// Brings the receivable in line with an edited expense: creates,
        /// re-values or removes it. Returns the 400 sentence when a bound would
        /// be broken.
        /// </summary>
        Task<string?> ApplyToUpdatedExpenseAsync(
            Expense expense, decimal? expectedReimbursement, CancellationToken cancellationToken = default);

        /// <summary>
        /// Undoes every settlement that landed on an account, so deleting a split
        /// expense leaves no money behind. The rows themselves go with the
        /// expense through the database cascade.
        /// </summary>
        Task<string?> ReverseForDeletedExpenseAsync(Expense expense, CancellationToken cancellationToken = default);
    }
}
