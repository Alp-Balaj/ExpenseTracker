using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Entities.Common;
using ExpenseTracker.Domain.Interfaces;
using ExpenseTracker.Shared.Enums;

namespace ExpenseTracker.Application.Services;

public sealed class CategoryService : ICategoryService
{
    private readonly IUnitOfWork _unitOfWork;

    public CategoryService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IEnumerable<CategorySummaryDTO>> GetCategorySummariesAsync(
        CategoryType categoryType)
    {
        var categories = await _unitOfWork.Categories.GetAllUserDataAsync();

        var filteredCategories = categories
            .Where(c => c.CategoryType == categoryType)
            .ToList();

        IEnumerable<BaseEntity> entries = categoryType switch
        {
            CategoryType.Expense =>
                await _unitOfWork.Expenses.GetAllUserDataAsync(),

            CategoryType.Income =>
                await _unitOfWork.Incomes.GetAllUserDataAsync(),

            CategoryType.Savings =>
                await _unitOfWork.Savings.GetAllUserDataAsync(),

            CategoryType.FutureExpense =>
                await _unitOfWork.FutureExpenses.GetAllUserDataAsync(),

            _ => throw new ArgumentOutOfRangeException(nameof(categoryType))
        };

        var totalsByCategoryId = entries
            .GroupBy(e => ((dynamic)e).CategoryId)
            .ToDictionary(g => g.Key, g => ((IEnumerable<dynamic>)g)
            .Sum(e => (decimal)e.Amount));

        return filteredCategories
            .Select(c => new CategorySummaryDTO
            {
                Id = c.Id,
                Name = c.Name,
                Color = c.Color,
                TotalAmount = totalsByCategoryId.GetValueOrDefault(c.Id, 0m)
            })
            .ToList();
    }
}
