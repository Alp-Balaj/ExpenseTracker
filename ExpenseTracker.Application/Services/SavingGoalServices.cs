using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces;

namespace ExpenseTracker.Application.Services;
public class SavingGoalServices: ISavingGoalServices
{
    private readonly IUserRelatedRepository<SavingGoal> _savingGoalRepo;
    public SavingGoalServices(IUserRelatedRepository<SavingGoal> savingGoalRepo)
    {
        _savingGoalRepo = savingGoalRepo;
    }
    public async Task<(bool Success, string ErrorMessage)> UpdateSavingGoalAmount(Guid goalId, decimal amount)
    {
        if(amount < 0)
            return (false, "Amount must be greater than zero.");
            
        var goal = await _savingGoalRepo.GetUserDataByIdAsync(goalId);

        if (goal == null)
            return (false, "Account not found");

        if (goal.UserId != _savingGoalRepo.GetCurrentUserId())
            return (false, "Account does not belong to this user.");

        goal.SavedAmount += amount;

        _savingGoalRepo.UpdateUserData(goal);

        return (true,"");
    }
}