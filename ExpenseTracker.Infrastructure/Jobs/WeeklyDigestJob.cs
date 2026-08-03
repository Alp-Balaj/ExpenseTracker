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
/// Trigger 3 of decision D3: what last week cost, against the week before.
/// <para>
/// The only one of the three that is not an alarm. It is the payoff loop the
/// roadmap is built around — the app asks for expenses all week and this is the
/// one moment it gives something back unprompted.
/// </para>
/// <para>
/// The window is the seven complete days ending yesterday, never a partial today:
/// a digest whose figure changes depending on what hour the container happened to
/// wake up is not comparable to the one before it.
/// </para>
/// </summary>
public sealed class WeeklyDigestJob : IScheduledJob
{
    private readonly ApplicationDbContext _context;
    private readonly IPushNotificationService _notifications;
    private readonly IPushSender _sender;
    private readonly ILogger<WeeklyDigestJob> _logger;

    public WeeklyDigestJob(
        ApplicationDbContext context,
        IPushNotificationService notifications,
        IPushSender sender,
        ILogger<WeeklyDigestJob> logger)
    {
        _context = context;
        _notifications = notifications;
        _sender = sender;
        _logger = logger;
    }

    public string Name => ScheduledJobNames.WeeklyDigest;

    public TimeSpan Interval => TimeSpan.FromDays(7);

    public bool Enabled => _sender.IsConfigured;

    public async Task<string> RunAsync(ScheduledJobContext context, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(context.NowUtc);

        // Deliberately *not* reconciled from LastSucceededAtUtc. A container that
        // was down for three weeks should send one digest about last week, not
        // three about weeks nobody can act on any more. The interval alone is the
        // right catch-up rule for this job; the dedupe key below is what makes
        // that safe.
        var weekEnd = today.AddDays(-1);
        var weekStart = weekEnd.AddDays(-6);
        var priorStart = weekStart.AddDays(-7);

        var userIds = await NotificationTargets.WithSubscriptionsAsync(_context, cancellationToken);
        var sent = 0;

        foreach (var userId in userIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                sent += await DigestUserAsync(userId, priorStart, weekStart, weekEnd, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Weekly digest failed for user {UserId}; continuing.", userId);
            }
        }

        return $"{sent} digest(s) sent for {weekStart:yyyy-MM-dd}..{weekEnd:yyyy-MM-dd}.";
    }

    private async Task<int> DigestUserAsync(
        string userId, DateOnly priorStart, DateOnly weekStart, DateOnly weekEnd, CancellationToken cancellationToken)
    {
        // Both fortnight halves in one query, then split in memory — two round
        // trips per user per week would be two for no reason.
        var rows = await _context.Expenses
            .Where(e => e.UserId == userId && e.Date >= priorStart && e.Date <= weekEnd)
            .GroupBy(e => new { e.Date, e.CategoryId, Code = e.Account.Currency.Code })
            .Select(g => new { g.Key.Date, g.Key.CategoryId, g.Key.Code, Total = g.Sum(e => e.Amount) })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0) return 0;

        var converter = await NotificationTargets.ConverterForAsync(_context, userId, cancellationToken);

        var thisWeek = rows.Where(r => r.Date >= weekStart).ToList();
        var priorWeek = rows.Where(r => r.Date < weekStart).ToList();

        var thisTotal = converter.Summarize(thisWeek.Select(r => new CurrencyAmount(r.Code, r.Total)));
        if (thisTotal.ConvertedTotal is not decimal spent) return 0;

        var priorTotal = converter.Summarize(priorWeek.Select(r => new CurrencyAmount(r.Code, r.Total)));

        var comparison = priorTotal.ConvertedTotal is decimal previous && previous > 0m
            ? Compare(spent, previous)
            : string.Empty;

        var topCategory = await TopCategoryAsync(userId, thisWeek
            .GroupBy(r => r.CategoryId)
            .Select(g => new
            {
                CategoryId = g.Key,
                Total = converter.Summarize(g.Select(r => new CurrencyAmount(r.Code, r.Total))).ConvertedTotal
            })
            .Where(g => g.Total is not null)
            .OrderByDescending(g => g.Total)
            .Select(g => g.CategoryId)
            .FirstOrDefault(), cancellationToken);

        var body = $"You spent {NotificationTargets.Money(spent, thisTotal.BaseCurrency)} last week{comparison}."
                   + (topCategory is null ? string.Empty : $" Most of it went on {topCategory}.");

        var delivered = await _notifications.SendToUserAsync(
            userId,
            NotificationKind.WeeklyDigest,
            // ISO week of the week being reported, so the turn of the year cannot
            // produce two digests claiming the same key.
            IsoWeekKey(weekEnd),
            new PushMessage("Your week in numbers", body, "/reports", "weekly-digest"),
            cancellationToken);

        return delivered > 0 ? 1 : 0;
    }

    private async Task<string?> TopCategoryAsync(string userId, Guid categoryId, CancellationToken cancellationToken)
    {
        if (categoryId == Guid.Empty) return null;

        return await _context.Categories
            .Where(c => c.UserId == userId && c.Id == categoryId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string Compare(decimal spent, decimal previous)
    {
        var change = (spent - previous) / previous * 100m;
        var rounded = Math.Round(Math.Abs(change), 0, MidpointRounding.AwayFromZero);

        // Under 5% is noise, and calling it a change makes every other week's
        // digest sound like an alarm.
        if (rounded < 5m) return ", about the same as the week before";

        return change > 0m
            ? $", {rounded:0}% more than the week before"
            : $", {rounded:0}% less than the week before";
    }

    private static string IsoWeekKey(DateOnly date)
    {
        var asDateTime = date.ToDateTime(TimeOnly.MinValue);
        var year = ISOWeek.GetYear(asDateTime).ToString(CultureInfo.InvariantCulture);
        var week = ISOWeek.GetWeekOfYear(asDateTime).ToString("00", CultureInfo.InvariantCulture);
        return $"{year}-W{week}";
    }
}
