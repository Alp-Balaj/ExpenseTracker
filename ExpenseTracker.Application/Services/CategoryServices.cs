using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Interfaces;
using ExpenseTracker.Shared.Enums;
using Microsoft.EntityFrameworkCore;

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

    /// <summary>
    /// One row per (category, currency) with the amount already summed by the
    /// database. A null <see cref="Currency"/> means the amount carries no
    /// currency of its own and is assumed to be in base.
    /// </summary>
    private sealed record CategoryTotal(Guid CategoryId, string? Currency, decimal Total);

    public async Task<IReadOnlyList<CategorySummaryDTO>> GetCategorySummariesAsync(
        CancellationToken cancellationToken = default)
    {
        // Grouped by type, as when the controller looped the enum and
        // concatenated a call per value.
        var categories = await _unitOfWork.Categories.Query()
            .OrderBy(c => c.CategoryType)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        if (categories.Count == 0) return [];

        // Four grouped aggregates, one per category type. Each is a single
        // GROUP BY in Postgres: the amounts are summed server-side, so what
        // comes back is a handful of rows rather than the user's whole history.
        var totalsByType = new Dictionary<CategoryType, ILookup<Guid, CategoryTotal>>
        {
            [CategoryType.Expense] = await ExpenseTotalsAsync(cancellationToken),
            [CategoryType.Income] = await IncomeTotalsAsync(cancellationToken),
            [CategoryType.SavingGoals] = await SavingGoalTotalsAsync(cancellationToken),
            [CategoryType.FutureExpense] = await FutureExpenseTotalsAsync(cancellationToken)
        };

        var converter = await _conversion.GetConverterAsync(cancellationToken);

        var summaries = new List<CategorySummaryDTO>(categories.Count);

        foreach (var category in categories)
        {
            var totals = totalsByType.TryGetValue(category.CategoryType, out var lookup)
                ? lookup[category.Id].ToList()
                : [];

            var breakdown = converter.Summarize(
                totals.Select(t => new CurrencyAmount(t.Currency, t.Total)));

            summaries.Add(new CategorySummaryDTO
            {
                Id = category.Id,
                Name = category.Name,
                Color = category.Color,
                TotalAmount = totals.Sum(t => t.Total),
                CategoryType = (int)category.CategoryType,
                Subtotals = breakdown.Subtotals,
                BaseCurrency = breakdown.BaseCurrency,
                ConvertedTotal = breakdown.ConvertedTotal,
                HasMissingRates = breakdown.HasMissingRates
            });
        }

        return summaries;
    }

    // All four project to an anonymous type and build the CategoryTotal after
    // the round trip. Constructing the record inside the SQL projection reads
    // better but does not survive translation intact.
    private async Task<ILookup<Guid, CategoryTotal>> ExpenseTotalsAsync(CancellationToken cancellationToken) =>
        (await _unitOfWork.Expenses.Query()
            .GroupBy(e => new { e.CategoryId, Code = e.Account.Currency.Code })
            .Select(g => new { g.Key.CategoryId, g.Key.Code, Total = g.Sum(e => e.Amount) })
            .ToListAsync(cancellationToken))
        .Select(row => new CategoryTotal(row.CategoryId, row.Code, row.Total))
        .ToLookup(t => t.CategoryId);

    private async Task<ILookup<Guid, CategoryTotal>> IncomeTotalsAsync(CancellationToken cancellationToken) =>
        (await _unitOfWork.Incomes.Query()
            .GroupBy(i => new { i.CategoryId, Code = i.Account.Currency.Code })
            .Select(g => new { g.Key.CategoryId, g.Key.Code, Total = g.Sum(i => i.Amount) })
            .ToListAsync(cancellationToken))
        .Select(row => new CategoryTotal(row.CategoryId, row.Code, row.Total))
        .ToLookup(t => t.CategoryId);

    private async Task<ILookup<Guid, CategoryTotal>> SavingGoalTotalsAsync(CancellationToken cancellationToken) =>
        (await _unitOfWork.SavingGoals.Query()
            .GroupBy(s => new { s.CategoryId, Code = s.Account.Currency.Code })
            .Select(g => new { g.Key.CategoryId, g.Key.Code, Total = g.Sum(s => s.SavedAmount) })
            .ToListAsync(cancellationToken))
        .Select(row => new CategoryTotal(row.CategoryId, row.Code, row.Total))
        .ToLookup(t => t.CategoryId);

    // FutureExpense has no account, hence no currency of its own: a null code
    // means the amount is assumed to already be in the user's base currency.
    private async Task<ILookup<Guid, CategoryTotal>> FutureExpenseTotalsAsync(CancellationToken cancellationToken) =>
        (await _unitOfWork.FutureExpenses.Query()
            .GroupBy(f => f.CategoryId)
            .Select(g => new { CategoryId = g.Key, Total = g.Sum(f => f.Amount) })
            .ToListAsync(cancellationToken))
        .Select(row => new CategoryTotal(row.CategoryId, null, row.Total))
        .ToLookup(t => t.CategoryId);
}
