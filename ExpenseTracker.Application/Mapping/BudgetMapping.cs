using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Shared.Enums;

namespace ExpenseTracker.Application.Mapping
{
    public static class BudgetMapping
    {
        public static BudgetDTO ToDto(this Budget entity)
        {
            return new BudgetDTO
            {
                Id = entity.Id,
                Amount = entity.Amount,
                Period = (int)entity.Period,
                Note = entity.Note,
                CategoryId = entity.CategoryId,
            };
        }

        public static Budget ToEntity(this BudgetDTO dto)
        {
            return new Budget
            {
                UserId = null!,

                Amount = dto.Amount,
                Period = (BudgetPeriod)dto.Period,
                Note = dto.Note,
                CategoryId = dto.CategoryId,
            };
        }

        public static void Apply(this Budget entity, BudgetDTO dto)
        {
            entity.Amount = dto.Amount;
            entity.Period = (BudgetPeriod)dto.Period;
            entity.Note = dto.Note;
            entity.CategoryId = dto.CategoryId;
        }
    }
}
