using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Domain.Entities;
namespace ExpenseTracker.Application.Mapping
{
    public static class SavingGoalMapping
    {
        public static SavingGoalDTO ToDto(this SavingGoal entity)
        {
            return new SavingGoalDTO
            {
               Id = entity.Id,
               Title = entity.Title,
               TargetAmount = entity.TargetAmount,
               SavedAmount = entity.SavedAmount,
               Deadline = entity.Deadline,
               Description = entity.Description,
               CategoryId = entity.CategoryId,
               AccountId = entity.AccountId,
            };
        }
        public static SavingGoal ToEntity(this SavingGoalDTO dto)
        {
            return new SavingGoal
            {
                UserId = null!,

                Title = dto.Title,
                TargetAmount = dto.TargetAmount,
                // SavedAmount is server-maintained: a new goal starts at 0 and
                // changes only through contributions.
                SavedAmount = 0,
                Deadline = dto.Deadline,
                Description = dto.Description,
                CategoryId = dto.CategoryId,
                AccountId = dto.AccountId,
            };
        }
        public static void Apply(this SavingGoal entity, SavingGoalDTO dto)
        {
            entity.Title = dto.Title;
            entity.TargetAmount = dto.TargetAmount;
            // SavedAmount is intentionally not applied: it changes only
            // through contributions.
            entity.Deadline = dto.Deadline;
            entity.Description = dto.Description;
            entity.CategoryId = dto.CategoryId;
            entity.AccountId = dto.AccountId;
        }
    }
}
