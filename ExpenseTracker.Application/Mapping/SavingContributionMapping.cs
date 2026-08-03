using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Domain.Entities;
namespace ExpenseTracker.Application.Mapping
{
    public static class SavingContributionMapping
    {
        public static SavingContributionDTO ToDto(this SavingContribution entity)
        {
            return new SavingContributionDTO
            {
               Id = entity.Id,
               GoalId = entity.GoalId,
               Amount = entity.Amount,
               Date = entity.Date,
               Note = entity.Note,
               AccountId = entity.AccountId,
            };
        }
        public static SavingContribution ToEntity(this SavingContributionDTO dto)
        {
            return new SavingContribution
            {
                UserId = null!,
                
                GoalId = dto.GoalId,
                Amount = dto.Amount,
                Date = dto.Date,
                Note = dto.Note,
                AccountId = dto.AccountId,
            };
        }
        public static void Apply(this SavingContribution entity, SavingContributionDTO dto)
        {
            entity.Amount = dto.Amount;
            entity.Date = dto.Date;
            entity.Note = dto.Note;
            entity.GoalId = dto.GoalId;
            entity.AccountId = dto.AccountId;
        }
    }
}
