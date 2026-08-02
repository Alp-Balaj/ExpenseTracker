using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Application.Services;

public sealed class InsightsService : IInsightsService
{
    /// <summary>How many months before the current one make up "your usual".</summary>
    private const int TrailingMonths = 3;

    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrencyConversionService _conversion;

    public InsightsService(IUnitOfWork unitOfWork, ICurrencyConversionService conversion)
    {
        _unitOfWork = unitOfWork;
        _conversion = conversion;
    }

    public async Task<InsightsDTO> GetInsightsAsync(CancellationToken cancellationToken = default)
    {
        // The same UTC "today" the planned-transaction schedule uses, so the two
        // dashboard features can never disagree about which month it is.
        var today = PlannedSchedule.Today;
        var firstOfCurrentMonth = new DateOnly(today.Year, today.Month, 1);

        // months[0] = the current month, months[1..3] = the trailing window.
        // One grouped SQL query per month, the exact shape BudgetService uses:
        // the month filter and the currency join both run in the database.
        var months = new List<List<SpendRow>>(TrailingMonths + 1);
        for (var offset = 0; offset <= TrailingMonths; offset++)
        {
            var first = firstOfCurrentMonth.AddMonths(-offset);
            var next = first.AddMonths(1);

            var rows = await _unitOfWork.Expenses.Query()
                .Where(e => e.Date >= first && e.Date < next)
                .GroupBy(e => new { e.CategoryId, Code = e.Account.Currency.Code })
                .Select(g => new SpendRow(g.Key.CategoryId, g.Key.Code, g.Sum(e => e.Amount)))
                .ToListAsync(cancellationToken);

            months.Add(rows);
        }

        var converter = await _conversion.GetConverterAsync(cancellationToken);
        var hasMissingRates = false;

        // Per month: the converted overall total and the converted per-category totals.
        var monthTotals = new decimal[TrailingMonths + 1];
        var byCategory = new Dictionary<Guid, decimal>[TrailingMonths + 1];
        for (var offset = 0; offset <= TrailingMonths; offset++)
        {
            monthTotals[offset] = CurrencyTotals.SumConvertible(
                converter,
                months[offset].Select(r => new CurrencyAmount(r.Code, r.Total)),
                ref hasMissingRates);

            byCategory[offset] = months[offset]
                .GroupBy(r => r.CategoryId)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var unused = false; // month total above already flagged the gap
                        return CurrencyTotals.SumConvertible(
                            converter,
                            g.Select(r => new CurrencyAmount(r.Code, r.Total)),
                            ref unused);
                    });
        }

        // A month "counts" as history when anything at all was recorded in it —
        // raw rows, not converted totals, so a missing rate cannot erase a month.
        var monthsWithHistory = Enumerable.Range(1, TrailingMonths).Count(i => months[i].Count > 0);

        // Averaged over the months that have data: two real months of spending
        // should not be diluted by an empty third into a rosier "usual".
        var trailingSum = Enumerable.Range(1, TrailingMonths).Sum(i => monthTotals[i]);
        decimal? trailingAverage = monthsWithHistory > 0
            ? Round2(trailingSum / monthsWithHistory)
            : null;

        var categories = (await _unitOfWork.Categories.GetAllUserDataAsync(cancellationToken))
            .ToDictionary(c => c.Id);

        var activeCategoryIds = byCategory.SelectMany(m => m.Keys).Distinct();

        var categoryInsights = new List<CategoryInsightDTO>();
        foreach (var categoryId in activeCategoryIds)
        {
            var current = Round2(byCategory[0].GetValueOrDefault(categoryId));
            var previous = Round2(byCategory[1].GetValueOrDefault(categoryId));

            var categoryTrailingSum = Enumerable.Range(1, TrailingMonths)
                .Sum(i => byCategory[i].GetValueOrDefault(categoryId));
            decimal? categoryAverage = monthsWithHistory > 0
                ? Round2(categoryTrailingSum / monthsWithHistory)
                : null;

            categories.TryGetValue(categoryId, out var category);

            categoryInsights.Add(new CategoryInsightDTO
            {
                CategoryId = categoryId,
                CategoryName = category?.Name ?? "Unknown",
                CategoryColor = category?.Color,
                CurrentMonthTotal = current,
                PreviousMonthTotal = previous,
                MomChangeAmount = current - previous,
                MomChangePct = PercentChange(current, previous),
                TrailingAverage = categoryAverage,
                VsAveragePct = categoryAverage is decimal avg ? PercentChange(current, avg) : null,
            });
        }

        return new InsightsDTO
        {
            Month = $"{today.Year:D4}-{today.Month:D2}",
            BaseCurrency = converter.BaseCurrency,
            HasMissingRates = hasMissingRates,
            MonthsWithHistory = monthsWithHistory,
            CurrentMonthTotal = Round2(monthTotals[0]),
            PreviousMonthTotal = Round2(monthTotals[1]),
            TrailingAverage = trailingAverage,
            OverallVsAveragePct = trailingAverage is decimal overallAvg
                ? PercentChange(Round2(monthTotals[0]), overallAvg)
                : null,
            // Biggest movers first — the frontend reads its sentences off the top.
            Categories = categoryInsights
                .OrderByDescending(c => Math.Abs(c.MomChangeAmount))
                .ThenByDescending(c => c.CurrentMonthTotal)
                .ThenBy(c => c.CategoryName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
    }

    /// <summary>Change from <paramref name="baseline"/>, as a percentage; null when the baseline is zero or negative.</summary>
    private static decimal? PercentChange(decimal current, decimal baseline) =>
        baseline > 0m ? Round2((current - baseline) / baseline * 100m) : null;

    private static decimal Round2(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private sealed record SpendRow(Guid CategoryId, string Code, decimal Total);
}
