using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Domain.Entities;
namespace ExpenseTracker.Application.Mapping
{
    public static class FutureExpenseMapping
    {
        public static FutureExpenseDTO ToDto(this FutureExpense entity)
        {
            return new FutureExpenseDTO
            {
                Id = entity.Id,
                Title = entity.Title,
                Amount = entity.Amount,
                Date = entity.PredictedDate,
                CategoryId = entity.CategoryId,
                Description = entity.Description,
            };
        }
        public static FutureExpense ToEntity(this FutureExpenseDTO dto)
        {
            return new FutureExpense
            {
                Title = dto.Title,
                Amount = dto.Amount,
                PredictedDate = dto.Date,
                CategoryId = dto.CategoryId,
                Description = dto.Description,
            };
        }
        public static void Apply(this FutureExpense entity, FutureExpenseDTO dto)
        {
            entity.Title = dto.Title;
            entity.Amount = dto.Amount;
            entity.PredictedDate = dto.Date;
            entity.CategoryId = dto.CategoryId;
            entity.Description = dto.Description;
        }
    }
}
