using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Domain.Entities;
namespace ExpenseTracker.Application.Mapping
{
    public static class SavingMapping
    {
        public static SavingDTO ToDto(this Saving entity)
        {
            return new SavingDTO
            {
               Id = entity.Id,
               Amount = entity.Amount,
               Date = entity.Date,
               Description = entity.Description,
               CategoryId = entity.CategoryId,
               AccountId = entity.AccountId,
            };
        }
        public static Saving ToEntity(this SavingDTO dto)
        {
            return new Saving
            {
                Amount = dto.Amount,
                Date = dto.Date,
                Description = dto.Description,
                CategoryId = dto.CategoryId,
                AccountId = dto.AccountId,
            };
        }
        public static void Apply(this Saving entity, SavingDTO dto)
        {
            entity.Amount = dto.Amount;
            entity.Date = dto.Date;
            entity.Description = dto.Description;
            entity.CategoryId = dto.CategoryId;
            entity.AccountId = dto.AccountId;
        }
    }
}
