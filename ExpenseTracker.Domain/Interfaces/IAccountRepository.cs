using ExpenseTracker.Domain.Entities;

namespace ExpenseTracker.Domain.Interfaces
{
    public interface IAccountRepository
    {
        /// <summary>
        /// Accounts with their currency loaded, untracked, windowed the same way
        /// as <see cref="IUserRelatedRepository{T}.GetUserDataPageAsync"/>:
        /// null <paramref name="take"/> returns every row.
        /// </summary>
        Task<(IReadOnlyList<Account> Items, int TotalCount)> GetUserDataPageWithCurrencyAsync(
            int? skip,
            int? take,
            CancellationToken cancellationToken = default);

        /// <summary>Tracked: also the read-back path after a create.</summary>
        Task<Account?> GetUserDataWithCurrencyByIdAsync(Guid id, CancellationToken cancellationToken = default);

        Task<bool> AdjustBalanceAsync(Guid accountId, decimal delta, CancellationToken cancellationToken = default);
    }
}
