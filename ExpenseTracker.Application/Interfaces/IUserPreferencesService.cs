using ExpenseTracker.Domain.Entities.User;

namespace ExpenseTracker.Application.Interfaces
{
    public interface IUserPreferencesService
    {
        Task<UserPreferences> GetAsync();
        Task UpdateAsync(UserPreferences updated);
    }
}
