using ExpenseTracker.Domain.Entities.Common;

namespace ExpenseTracker.Domain.Interfaces
{
    public interface IUserRelatedRepository<T> where T : BaseEntity
    {
        Task<IEnumerable<T>> GetAllUserDataAsync();
        Task<T?> GetUserDataByIdAsync(Guid id);
        Task AddAsync(T entity);
        void UpdateUserData(T entity);
        void DeleteUserData(T entity);
        IQueryable<T> Query();
        string GetCurrentUserId();
    }
}
