using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Domain.Entities;
namespace ExpenseTracker.Application.Mapping
{
    public static class IncomeMapping
    {
        public static IncomeDTO ToDto(this Income entity)
        {
            return new IncomeDTO
            {
                Id = entity.Id,
                Amount = entity.Amount,
                Date = entity.Date,
                Description = entity.Description,
                AccountId = entity.AccountId,
                CategoryId = entity.CategoryId,
            };
        }
        public static Income ToEntity(this IncomeDTO dto)
        {
            return new Income
            {
                Amount = dto.Amount,
                Date = dto.Date,
                Description = dto.Description,
                AccountId = dto.AccountId,
                CategoryId = dto.CategoryId,
            };
        }
        public static void Apply(this Income entity, IncomeDTO dto)
        {
            entity.Amount = dto.Amount;
            entity.Date = dto.Date;
            entity.Description = dto.Description;
            entity.AccountId = dto.AccountId;
            entity.CategoryId = dto.CategoryId;
        }
    }
}
