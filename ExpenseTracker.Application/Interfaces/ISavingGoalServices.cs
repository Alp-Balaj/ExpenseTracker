using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.Application.Interfaces;
public interface ISavingGoalServices
{
    Task<(bool Success, string ErrorMessage)> UpdateSavingGoalAmount(Guid goalId, decimal amount);
}