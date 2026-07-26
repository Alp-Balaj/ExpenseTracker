using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Interfaces;
using ExpenseTracker.Shared.Enums;

namespace ExpenseTracker.Application.Services;

public sealed class CategoryService : ICategoryService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrencyConversionService _conversion;

    public CategoryService(IUnitOfWork unitOfWork, ICurrencyConversionService conversion)
    {
        _unitOfWork = unitOfWork;
        _conversion = conversion;
    }

    public async Task<IEnumerable<CategorySummaryDTO>> GetCategorySummariesAsync(
        CategoryType categoryType)
    {
        var categories = await _unitOfWork.Categories.GetAllUserDataAsync();

        var filteredCategories = categories
            .Where(c => c.CategoryType == categoryType)
            .ToList();

        var accounts = await _unitOfWork.Accounts.GetAllUserDataAsync();
        var currencies = await _unitOfWork.Currencies.GetAllUserDataAsync();

        var codeByCurrencyId = currencies.ToDictionary(c => c.Id, c => c.Code);
        var codeByAccountId = accounts.ToDictionary(
            a => a.Id,
            a => codeByCurrencyId.GetValueOrDefault(a.CurrencyId));

        // FutureExpense has no account, hence no currency: a null code means the
        // amount is assumed to already be in the user's base currency.
        List<(Guid CategoryId, string? Currency, decimal Amount)> entries = categoryType switch
        {
            CategoryType.Expense =>
                (await _unitOfWork.Expenses.GetAllUserDataAsync())
                    .Select(e => (e.CategoryId, codeByAccountId.GetValueOrDefault(e.AccountId), e.Amount))
                    .ToList(),

            CategoryType.Income =>
                (await _unitOfWork.Incomes.GetAllUserDataAsync())
                    .Select(i => (i.CategoryId, codeByAccountId.GetValueOrDefault(i.AccountId), i.Amount))
                    .ToList(),

            CategoryType.SavingGoals =>
                (await _unitOfWork.SavingGoals.GetAllUserDataAsync())
                    .Select(s => (s.CategoryId, codeByAccountId.GetValueOrDefault(s.AccountId), s.SavedAmount))
                    .ToList(),

            CategoryType.FutureExpense =>
                (await _unitOfWork.FutureExpenses.GetAllUserDataAsync())
                    .Select(f => (f.CategoryId, (string?)null, f.Amount))
                    .ToList(),

            _ => throw new ArgumentOutOfRangeException(nameof(categoryType))
        };

        var entriesByCategoryId = entries.ToLookup(e => e.CategoryId);
        var converter = await _conversion.GetConverterAsync();

        return filteredCategories
            .Select(c =>
            {
                var categoryEntries = entriesByCategoryId[c.Id].ToList();
                var breakdown = converter.Summarize(
                    categoryEntries.Select(e => new CurrencyAmount(e.Currency, e.Amount)));

                return new CategorySummaryDTO
                {
                    Id = c.Id,
                    Name = c.Name,
                    Color = c.Color,
                    TotalAmount = categoryEntries.Sum(e => e.Amount),
                    CategoryType = (int)c.CategoryType,
                    Subtotals = breakdown.Subtotals,
                    BaseCurrency = breakdown.BaseCurrency,
                    ConvertedTotal = breakdown.ConvertedTotal,
                    HasMissingRates = breakdown.HasMissingRates
                };
            })
            .ToList();
    }
}
