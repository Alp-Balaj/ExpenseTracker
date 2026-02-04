using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Entities.User;
using ExpenseTracker.Domain.Interfaces;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.Application.Services;
public class AccountServices
{
    private readonly IUserRelatedRepository<Account> _accountRepo;
    public AccountServices(IUserRelatedRepository<Account> accountRepo)
    {
        _accountRepo = accountRepo;
    }
    public async Task<(bool Success, string ErrorMessage)> DecrementAmountFromAccount(decimal amount, Guid accountId)
    {
        if(amount < 0)
            return (false, "Amount must be greater than zero.");
            
        var account = await _accountRepo.GetUserDataByIdAsync(accountId);

        if (account == null)
            return (false, "Account not found");

        if (account.UserId != _accountRepo.GetCurrentUserId())
            return (false, "Account does not belong to this user.");

        account.Balance -= amount;

        _accountRepo.UpdateUserData(account);

        return (true,"");
    }
}