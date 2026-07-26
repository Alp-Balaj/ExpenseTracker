using ExpenseTracker.Domain.Entities;

namespace ExpenseTracker.Domain.Interfaces
{
    public interface IAccountRepository
    {
        Task<IEnumerable<Account>> GetAllUserDataWithCurrencyAsync();
        Task<bool> AdjustBalanceAsync(Guid accountId, decimal delta);
    }
}
