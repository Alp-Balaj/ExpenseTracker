using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Interfaces;
using System.Globalization;

namespace ExpenseTracker.Application.Services;

public sealed class BudgetService : IBudgetService
{
    /// <summary>Name reported for the budget that spans every expense category.</summary>
    public const string OverallBudgetName = "Overall";

    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrencyConversionService _conversion;

    public BudgetService(IUnitOfWork unitOfWork, ICurrencyConversionService conversion)
    {
        _unitOfWork = unitOfWork;
        _conversion = conversion;
    }

    public async Task<IReadOnlyList<BudgetStatusDTO>> GetStatusAsync(int year, int month)
    {
        var budgets = (await _unitOfWork.Budgets.GetAllUserDataAsync()).ToList();
        if (budgets.Count == 0) return [];

        var monthLabel = $"{year:D4}-{month:D2}";
        var firstDay = new DateOnly(year, month, 1);
        var firstDayOfNextMonth = firstDay.AddMonths(1);

        var expenses = (await _unitOfWork.Expenses.GetAllUserDataAsync())
            .Where(e => e.Date >= firstDay && e.Date < firstDayOfNextMonth)
            .ToList();

        // An expense carries its account's currency; budgets are always in the
        // user's base currency, so spend has to be converted before comparison.
        var accounts = await _unitOfWork.Accounts.GetAllUserDataAsync();
        var currencies = await _unitOfWork.Currencies.GetAllUserDataAsync();

        var codeByCurrencyId = currencies.ToDictionary(c => c.Id, c => c.Code);
        var codeByAccountId = accounts.ToDictionary(
            a => a.Id,
            a => codeByCurrencyId.GetValueOrDefault(a.CurrencyId));

        var categories = (await _unitOfWork.Categories.GetAllUserDataAsync())
            .ToDictionary(c => c.Id);

        var amountsByCategoryId = expenses
            .ToLookup(
                e => e.CategoryId,
                e => new CurrencyAmount(codeByAccountId.GetValueOrDefault(e.AccountId), e.Amount));

        var allAmounts = expenses
            .Select(e => new CurrencyAmount(codeByAccountId.GetValueOrDefault(e.AccountId), e.Amount))
            .ToList();

        var converter = await _conversion.GetConverterAsync();

        var statuses = new List<BudgetStatusDTO>(budgets.Count);

        foreach (var budget in budgets)
        {
            IEnumerable<CurrencyAmount> amounts = budget.CategoryId is Guid categoryId
                ? amountsByCategoryId[categoryId]
                : allAmounts;

            var breakdown = converter.Summarize(amounts);

            // ConvertedTotal is null when a rate is missing or no base currency
            // is set; treat the convertible part as the spend and flag the gap
            // rather than reporting a budget as untouched.
            var spent = breakdown.ConvertedTotal ?? 0m;

            var name = budget.CategoryId is Guid id && categories.TryGetValue(id, out var category)
                ? category.Name
                : OverallBudgetName;

            statuses.Add(new BudgetStatusDTO
            {
                Id = budget.Id,
                CategoryId = budget.CategoryId,
                CategoryName = name,
                CategoryColor = budget.CategoryId is Guid colorId && categories.TryGetValue(colorId, out var c)
                    ? c.Color
                    : null,
                Note = budget.Note,
                Period = (int)budget.Period,
                Month = monthLabel,
                Limit = budget.Amount,
                Spent = spent,
                Remaining = budget.Amount - spent,
                PercentUsed = budget.Amount > 0m
                    ? Math.Round(spent / budget.Amount * 100m, 2, MidpointRounding.AwayFromZero)
                    : 0m,
                BaseCurrency = breakdown.BaseCurrency ?? converter.BaseCurrency,
                Subtotals = breakdown.Subtotals,
                HasMissingRates = breakdown.HasMissingRates,
            });
        }

        // Overall first, then the most-consumed budgets — the order the
        // dashboard widget and the report both want.
        return statuses
            .OrderByDescending(s => s.CategoryId is null)
            .ThenByDescending(s => s.PercentUsed)
            .ThenBy(s => s.CategoryName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Parses the <c>yyyy-MM</c> wire format used by the status endpoint.
    /// Returns false for anything else, including yyyy-MM-dd.
    /// </summary>
    public static bool TryParseMonth(string value, out int year, out int month)
    {
        year = 0;
        month = 0;

        if (!DateTime.TryParseExact(
                value,
                "yyyy-MM",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
            return false;

        year = parsed.Year;
        month = parsed.Month;
        return true;
    }
}
