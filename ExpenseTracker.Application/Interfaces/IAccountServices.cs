using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.Application.Interfaces;
public interface IAccountServices
{
    Task<(bool Success, string ErrorMessage)> DecrementAmountFromAccount(decimal amount, Guid accountId);
}