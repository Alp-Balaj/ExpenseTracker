using System.Globalization;
using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Application.Services;
using ExpenseTracker.Infrastructure.Persistence;
using ExpenseTracker.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExpenseTracker.Infrastructure.Jobs;

/// <summary>
/// Trigger 1 of decision D3: a budget has crossed a threshold it had not crossed
/// this month.
/// <para>
/// The useful moment for a budget warning is <em>before</em> the money is gone,
/// so 80% is the one that changes behaviour and 100% is the one that has to be
/// said out loud. Nothing between, and nothing after — 140% is not news.
/// </para>
/// <para>
/// The dedupe key carries the month and the threshold, so crossing 80% on the 5th
/// is said once, crossing 100% on the 20th is said once more, and the first of
/// next month starts clean. Without that this job would repeat itself every
/// single day for the rest of the month, which is precisely how a notification
/// permission gets revoked and never granted again.
/// </para>
/// </summary>
public sealed class BudgetThresholdJob : IScheduledJob
{
    /// <summary>Ascending. Only the highest newly crossed one is announced.</summary>
    private static readonly decimal[] Thresholds = [80m, 100m];

    private readonly ApplicationDbContext _context;
    private readonly IPushNotificationService _notifications;
    private readonly IPushSender _sender;
    private readonly ILogger<BudgetThresholdJob> _logger;

    public BudgetThresholdJob(
        ApplicationDbContext context,
        IPushNotificationService notifications,
        IPushSender sender,
        ILogger<BudgetThresholdJob> logger)
    {
        _context = context;
        _notifications = notifications;
        _sender = sender;
        _logger = logger;
    }

    public string Name => ScheduledJobNames.BudgetThresholds;

    public TimeSpan Interval => TimeSpan.FromHours(24);

    public bool Enabled => _sender.IsConfigured;

    public async Task<string> RunAsync(ScheduledJobContext context, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(context.NowUtc);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var nextMonth = monthStart.AddMonths(1);
        var monthLabel = monthStart.ToString("yyyy-MM", CultureInfo.InvariantCulture);

        var userIds = await NotificationTargets.WithSubscriptionsAsync(_context, cancellationToken);
        var sent = 0;

        foreach (var userId in userIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                sent += await EvaluateUserAsync(userId, monthStart, nextMonth, monthLabel, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Budget threshold evaluation failed for user {UserId}; continuing.", userId);
            }
        }

        return $"{sent} budget warning(s) sent across {userIds.Count} subscribed user(s).";
    }

    private async Task<int> EvaluateUserAsync(
        string userId, DateOnly monthStart, DateOnly nextMonth, string monthLabel, CancellationToken cancellationToken)
    {
        var budgets = await _context.Budgets
            .Where(b => b.UserId == userId)
            .Select(b => new { b.Id, b.Amount, b.CategoryId })
            .ToListAsync(cancellationToken);

        if (budgets.Count == 0) return 0;

        // The same grouped query BudgetService runs for the dashboard: one row per
        // (category, currency) for the month, converted afterwards.
        var monthSpend = await _context.Expenses
            .Where(e => e.UserId == userId && e.Date >= monthStart && e.Date < nextMonth)
            .GroupBy(e => new { e.CategoryId, Code = e.Account.Currency.Code })
            .Select(g => new { g.Key.CategoryId, g.Key.Code, Total = g.Sum(e => e.Amount) })
            .ToListAsync(cancellationToken);

        if (monthSpend.Count == 0) return 0;

        var converter = await NotificationTargets.ConverterForAsync(_context, userId, cancellationToken);

        var byCategory = monthSpend.ToLookup(row => row.CategoryId, row => new CurrencyAmount(row.Code, row.Total));
        var all = monthSpend.Select(row => new CurrencyAmount(row.Code, row.Total)).ToList();

        var categoryNames = await _context.Categories
            .Where(c => c.UserId == userId)
            .Select(c => new { c.Id, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        var sent = 0;

        foreach (var budget in budgets)
        {
            if (budget.Amount <= 0m) continue;

            var amounts = budget.CategoryId is Guid categoryId ? byCategory[categoryId] : all;
            var breakdown = converter.Summarize(amounts);

            // A missing rate understates the spend, so a warning would be wrong in
            // the safe direction — but silently. Skip rather than under-report:
            // the dashboard already flags the gap where the user can see it.
            if (breakdown.HasMissingRates || breakdown.ConvertedTotal is not decimal spent) continue;

            var percent = spent / budget.Amount * 100m;

            var crossed = Thresholds.Where(t => percent >= t).DefaultIfEmpty(0m).Max();
            if (crossed == 0m) continue;

            var name = budget.CategoryId is Guid id && categoryNames.TryGetValue(id, out var categoryName)
                ? categoryName
                : BudgetService.OverallBudgetName;

            var body = crossed >= 100m
                ? $"{name} is over budget: {NotificationTargets.Money(spent, breakdown.BaseCurrency)} of {NotificationTargets.Money(budget.Amount, breakdown.BaseCurrency)}."
                : $"{name} is at {Math.Round(percent, 0, MidpointRounding.AwayFromZero)}% of its budget — {NotificationTargets.Money(budget.Amount - spent, breakdown.BaseCurrency)} left this month.";

            var delivered = await _notifications.SendToUserAsync(
                userId,
                NotificationKind.BudgetThreshold,
                // Budget, month, threshold. Crossing 100% later in the same month
                // is a different key and is worth saying; the same 80% tomorrow is
                // not.
                $"{budget.Id}:{monthLabel}:{crossed:0}",
                new PushMessage(
                    crossed >= 100m ? "Over budget" : "Budget warning",
                    body,
                    "/budgets",
                    $"budget-{budget.Id}"),
                cancellationToken);

            if (delivered > 0) sent++;
        }

        return sent;
    }
}
