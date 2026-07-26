using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Interfaces;

namespace ExpenseTracker.Application.Services;
public class AccountServices: IAccountServices
{
    private readonly IAccountRepository _accountRepo;
    public AccountServices(IAccountRepository accountRepo)
    {
        _accountRepo = accountRepo;
    }
    public async Task<(bool Success, string ErrorMessage)> DecrementAmountFromAccount(decimal amount, Guid accountId)
    {
        if (amount <= 0)
            return (false, "Amount must be greater than zero.");

        return await AdjustAccountBalance(-amount, accountId);
    }
    public async Task<(bool Success, string ErrorMessage)> IncrementAmountToAccount(decimal amount, Guid accountId)
    {
        if (amount <= 0)
            return (false, "Amount must be greater than zero.");

        return await AdjustAccountBalance(amount, accountId);
    }
    public async Task<(bool Success, string ErrorMessage)> AdjustAccountBalance(decimal delta, Guid accountId)
    {
        if (delta == 0)
            return (true, "");

        // Atomic UPDATE (Balance = Balance + delta) scoped to the current user,
        // so concurrent requests can't lose an adjustment.
        var accountFound = await _accountRepo.AdjustBalanceAsync(accountId, delta);

        if (!accountFound)
            return (false, "Account not found");

        return (true, "");
    }
}
