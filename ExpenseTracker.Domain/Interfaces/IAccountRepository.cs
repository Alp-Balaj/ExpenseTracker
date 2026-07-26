using ExpenseTracker.Domain.Entities;

namespace ExpenseTracker.Domain.Interfaces
{
    public interface IAccountRepository
    {
        Task<IEnumerable<Account>> GetAllUserDataWithCurrencyAsync();
        Task<Account?> GetUserDataWithCurrencyByIdAsync(Guid id);
        Task<bool> AdjustBalanceAsync(Guid accountId, decimal delta);
    }
}
