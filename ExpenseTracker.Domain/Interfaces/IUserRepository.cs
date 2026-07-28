using ExpenseTracker.Domain.Entities.Common;

namespace ExpenseTracker.Domain.Interfaces
{
    public interface IUserRelatedRepository<T> where T : BaseEntity
    {
        /// <summary>
        /// Every row owned by the current user, untracked. Read-only by
        /// contract: callers that mean to change a row load it through
        /// <see cref="GetUserDataByIdAsync"/> instead.
        /// </summary>
        Task<IEnumerable<T>> GetAllUserDataAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// One window of the current user's rows, untracked, plus the total the
        /// window was taken from. A null <paramref name="take"/> means "no
        /// window" and returns everything — paging is opt-in, so a caller that
        /// asks for nothing gets the whole list exactly as it always did.
        /// </summary>
        /// <param name="orderBy">
        /// Ordering to page over. A window without a deterministic order is
        /// meaningless, so null falls back to newest-created first.
        /// </param>
        Task<(IReadOnlyList<T> Items, int TotalCount)> GetUserDataPageAsync(
            int? skip,
            int? take,
            Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy = null,
            CancellationToken cancellationToken = default);

        /// <summary>Tracked: this is the load path for update and delete.</summary>
        Task<T?> GetUserDataByIdAsync(Guid id, CancellationToken cancellationToken = default);

        Task AddAsync(T entity, CancellationToken cancellationToken = default);
        void UpdateUserData(T entity);
        void DeleteUserData(T entity);

        /// <summary>Untracked, user-scoped queryable for read-only projections.</summary>
        IQueryable<T> Query();

        string GetCurrentUserId();
    }
}
