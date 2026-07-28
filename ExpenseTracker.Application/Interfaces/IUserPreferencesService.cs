using ExpenseTracker.Domain.Entities.User;

namespace ExpenseTracker.Application.Interfaces
{
    public interface IUserPreferencesService
    {
        Task<UserPreferences> GetAsync(CancellationToken cancellationToken = default);
        Task UpdateAsync(UserPreferences updated, CancellationToken cancellationToken = default);
    }
}
