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
    private readonly IPotRepository _pots;

    public CategoryService(
        IUnitOfWork unitOfWork,
        ICurrencyConversionService conversion,
        IPotRepository pots)
    {
        _unitOfWork = unitOfWork;
        _conversion = conversion;
        _pots = pots;
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

        // One grouped aggregate per category type. Each is a single GROUP BY in
        // Postgres: the amounts are summed server-side, so what comes back is a
        // handful of rows rather than the user's whole history.
        //
        // Planned transactions are deliberately absent. They now share these
        // same Expense/Income categories (Sugg-4 retired the separate
        // CategoryType.FutureExpense axis), so counting them here would inflate
        // a category with money that has not moved — and then count it a second
        // time the moment the occurrence is committed.
        var totalsByType = new Dictionary<CategoryType, ILookup<Guid, CategoryTotal>>
        {
            [CategoryType.Expense] = await ExpenseTotalsAsync(cancellationToken),
            [CategoryType.Income] = await IncomeTotalsAsync(cancellationToken),
            [CategoryType.SavingGoals] = await SavingGoalTotalsAsync(cancellationToken)
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
                SystemKind = category.SystemKind,
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

    /// <summary>
    /// Net of what other people owe on a split bill (Stream R), plus the user's
    /// share of what their pots spent (Cycle U W2). A category total is an
    /// analysis figure — "what this category cost me" — so it counts the user's
    /// own share, taken on the date of the expense. The gross figure that left
    /// the account still lives on the account's balance.
    ///
    /// <para>
    /// The two adjustments are the same idea applied at two scales: R nets off
    /// what a friend owes on one bill, and the pot share is what one bill cost
    /// <em>you</em> out of what the group spent. Both land on the expense's own
    /// date, so neither total twitches when somebody settles up later.
    /// </para>
    /// </summary>
    private async Task<ILookup<Guid, CategoryTotal>> ExpenseTotalsAsync(CancellationToken cancellationToken)
    {
        var personal = await _unitOfWork.Expenses.Query()
            .GroupBy(e => new { e.CategoryId, Code = e.Account.Currency.Code })
            .Select(g => new CategoryTotal(
                g.Key.CategoryId,
                g.Key.Code,
                g.Sum(e => e.Amount - (e.Reimbursement == null ? 0m : e.Reimbursement.ExpectedAmount))))
            .ToListAsync(cancellationToken);

        // The reserved "Shared" category's own total. Grouped by the *pot's*
        // currency, because that is what the amount is denominated in — the
        // conversion into base then happens through this user's own rates, in
        // the same converter every other total goes through. A user in no pots
        // has no shares, so this is an empty list and every figure above is
        // exactly what it was before this wave.
        var shares = await _pots.SharesOfCurrentUser()
            .GroupBy(s => new { s.CategoryId, Code = s.Pot.CurrencyCode })
            .Select(g => new CategoryTotal(g.Key.CategoryId, g.Key.Code, g.Sum(s => s.Amount)))
            .ToListAsync(cancellationToken);

        return personal.Concat(shares).ToLookup(t => t.CategoryId);
    }

    private async Task<ILookup<Guid, CategoryTotal>> IncomeTotalsAsync(CancellationToken cancellationToken) =>
        (await _unitOfWork.Incomes.Query()
            .GroupBy(i => new { i.CategoryId, Code = i.Account.Currency.Code })
            .Select(g => new CategoryTotal(g.Key.CategoryId, g.Key.Code, g.Sum(i => i.Amount)))
            .ToListAsync(cancellationToken))
        .ToLookup(t => t.CategoryId);

    /// <summary>
    /// Stream V: a goal's currency is its earliest contribution's account's, not
    /// the deprecated single backing account's — which post-V goals may not have
    /// at all, and which would otherwise group a real total under a null code.
    /// Grouping happens after materialising because that first-contribution
    /// lookup is a correlated subquery, and the row count here is the user's
    /// goals, not their history.
    /// </summary>
    private async Task<ILookup<Guid, CategoryTotal>> SavingGoalTotalsAsync(CancellationToken cancellationToken) =>
        (await _unitOfWork.SavingGoals.Query()
            .Select(s => new
            {
                s.CategoryId,
                Code = s.SavingContributions
                    .OrderBy(c => c.Date).ThenBy(c => c.CreatedAt).ThenBy(c => c.Id)
                    .Select(c => c.Account!.Currency.Code)
                    .FirstOrDefault(),
                s.SavedAmount
            })
            .ToListAsync(cancellationToken))
        // A goal with no contributions has nothing to report and no currency to
        // report it in — its SavedAmount only ever moves through contributions.
        .Where(s => s.Code != null)
        .GroupBy(s => new { s.CategoryId, s.Code })
        .Select(g => new CategoryTotal(g.Key.CategoryId, g.Key.Code!, g.Sum(s => s.SavedAmount)))
        .ToLookup(t => t.CategoryId);
}
