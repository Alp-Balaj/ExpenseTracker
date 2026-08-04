using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace ExpenseTracker.Application.Services;

public sealed class BudgetService : IBudgetService
{
    /// <summary>Name reported for the budget that spans every expense category.</summary>
    public const string OverallBudgetName = "Overall";

    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrencyConversionService _conversion;
    private readonly IPotRepository _pots;

    public BudgetService(
        IUnitOfWork unitOfWork,
        ICurrencyConversionService conversion,
        IPotRepository pots)
    {
        _unitOfWork = unitOfWork;
        _conversion = conversion;
        _pots = pots;
    }

    public async Task<IReadOnlyList<BudgetStatusDTO>> GetStatusAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        var budgets = (await _unitOfWork.Budgets.GetAllUserDataAsync(cancellationToken)).ToList();
        if (budgets.Count == 0) return [];

        var monthLabel = $"{year:D4}-{month:D2}";
        var firstDay = new DateOnly(year, month, 1);
        var firstDayOfNextMonth = firstDay.AddMonths(1);

        // An expense carries its account's currency; budgets are always in the
        // user's base currency, so spend has to be converted before comparison.
        // The month filter and the currency join both run in SQL — this used to
        // pull every expense the user ever recorded to measure one month.
        //
        // Stream R: a budget measures the user's own share, so what is expected
        // back from other people comes off the top, on the day of the *expense*.
        // A budget therefore does not twitch when a friend finally pays up.
        var monthSpend = await _unitOfWork.Expenses.Query()
            .Where(e => e.Date >= firstDay && e.Date < firstDayOfNextMonth)
            .GroupBy(e => new { e.CategoryId, Code = e.Account.Currency.Code })
            .Select(g => new
            {
                g.Key.CategoryId,
                g.Key.Code,
                Total = g.Sum(e => e.Amount - (e.Reimbursement == null ? 0m : e.Reimbursement.ExpectedAmount))
            })
            .ToListAsync(cancellationToken);

        // Cycle U W2: the user's share of what their pots spent, in the month
        // the pot expense is dated — not the month it was typed, and not the
        // month anybody settles up in. Exactly the discipline the line above
        // applies to a split bill, one scale out: a top-up moves no budget
        // because nothing has been consumed yet, and the share does because
        // something has.
        //
        // Grouped by the pot's currency, then converted through this user's own
        // rates by the same converter as everything else — group screens never
        // convert, but a budget is not a group screen.
        //
        // A user in no pots has no shares, so this list is empty and every
        // figure below is to the cent what it was before this wave.
        var potShare = await _pots.SharesOfCurrentUser()
            .Where(s => s.PotExpense.Date >= firstDay && s.PotExpense.Date < firstDayOfNextMonth)
            .GroupBy(s => new { s.CategoryId, Code = s.Pot.CurrencyCode })
            .Select(g => new
            {
                g.Key.CategoryId,
                g.Key.Code,
                Total = g.Sum(s => s.Amount)
            })
            .ToListAsync(cancellationToken);

        monthSpend = monthSpend.Concat(potShare).ToList();

        var categories = (await _unitOfWork.Categories.GetAllUserDataAsync(cancellationToken))
            .ToDictionary(c => c.Id);

        var amountsByCategoryId = monthSpend
            .ToLookup(
                row => row.CategoryId,
                row => new CurrencyAmount(row.Code, row.Total));

        var allAmounts = monthSpend
            .Select(row => new CurrencyAmount(row.Code, row.Total))
            .ToList();

        var converter = await _conversion.GetConverterAsync(cancellationToken);

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
