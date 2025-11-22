using ExpenseTracker.Domain.Entities.Common;

namespace ExpenseTracker.Domain.Interfaces
{
    public interface IUserRepository<T> where T : BaseEntity
    {
        Task<IEnumerable<BaseEntity>> GetAllUserDataAsync();
        Task<BaseEntity?> GetUserDataByIdAsync(Guid id);
        Task AddAsync(T entity);
        void UpdateUserData(T entity);
        void DeleteUserData(T entity);
    }
}
