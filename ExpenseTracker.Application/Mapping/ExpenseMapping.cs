using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Shared.Enums;
namespace ExpenseTracker.Application.Mapping
{
    public static class ExpenseMapping
    {
        public static ExpenseDTO ToDto(this Expense entity)
        {
            return new ExpenseDTO
            {
                Id = entity.Id,
                Title = entity.Title,
                Amount = entity.Amount,
                Date = entity.Date,
                Description = entity.Description,
                AccountId = entity.AccountId,
                CategoryId = entity.CategoryId
            };
        }
        public static Expense ToEntity(this ExpenseDTO dto)
        {
            return new Expense
            {
                Title = dto.Title,
                Amount = dto.Amount,
                Date = dto.Date,
                Description = dto.Description,
                AccountId = dto.AccountId,
                CategoryId = dto.CategoryId
            };
        }
        public static void Apply(this Expense entity, ExpenseDTO dto)
        {
            entity.Title = dto.Title;
            entity.Amount = dto.Amount;
            entity.Date = dto.Date;
            entity.Description = dto.Description;
            entity.AccountId = dto.AccountId;
            entity.CategoryId = dto.CategoryId;
        }
    }
}
