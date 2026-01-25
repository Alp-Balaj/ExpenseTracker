using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Shared.Enums;

namespace ExpenseTracker.Application.Interfaces;
public interface ICategoryService
{
    Task<IEnumerable<CategorySummaryDTO>> GetCategorySummariesAsync(CategoryType categoryType);
}

