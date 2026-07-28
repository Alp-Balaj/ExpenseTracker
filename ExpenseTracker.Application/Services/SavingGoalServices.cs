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
    // Applies a signed delta to the goal's SavedAmount. Negative deltas are
    // legal here (contribution edit/delete reversal); the "amount > 0" rule
    // for a contribution itself is enforced by the controller.
    public async Task<(bool Success, string ErrorMessage)> UpdateSavingGoalAmount(Guid goalId, decimal delta, CancellationToken cancellationToken = default)
    {
        if (delta == 0)
            return (true, "");

        var goal = await _savingGoalRepo.GetUserDataByIdAsync(goalId, cancellationToken);

        if (goal == null)
            return (false, "Saving goal not found");

        if (goal.UserId != _savingGoalRepo.GetCurrentUserId())
            return (false, "Saving goal does not belong to this user.");

        goal.SavedAmount += delta;

        _savingGoalRepo.UpdateUserData(goal);

        return (true,"");
    }
}
