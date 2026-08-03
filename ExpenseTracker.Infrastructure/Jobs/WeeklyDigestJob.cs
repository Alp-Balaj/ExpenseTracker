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
    /// <summary>
    /// How long a receivable may sit before the digest mentions it. The same
    /// fortnight the cashflow projection assumes a split bill takes to come
    /// back, so the two features never disagree about when "late" starts.
    /// </summary>
    private const int AgedReceivableDays = 14;

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
                sent += await DigestUserAsync(userId, today, priorStart, weekStart, weekEnd, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Weekly digest failed for user {UserId}; continuing.", userId);
            }
        }

        return $"{sent} digest(s) sent for {weekStart:yyyy-MM-dd}..{weekEnd:yyyy-MM-dd}.";
    }

    private async Task<int> DigestUserAsync(
        string userId,
        DateOnly today,
        DateOnly priorStart,
        DateOnly weekStart,
        DateOnly weekEnd,
        CancellationToken cancellationToken)
    {
        // Both fortnight halves in one query, then split in memory — two round
        // trips per user per week would be two for no reason.
        // Net of split bills (Stream R): "you spent X last week" is a claim about
        // the user's own money, so what other people are expected to pay back
        // comes off on the day of the expense — never on the day they settle,
        // which would make a quiet week look like a windfall.
        var rows = await _context.Expenses
            .Where(e => e.UserId == userId && e.Date >= priorStart && e.Date <= weekEnd)
            .GroupBy(e => new { e.Date, e.CategoryId, Code = e.Account.Currency.Code })
            .Select(g => new
            {
                g.Key.Date,
                g.Key.CategoryId,
                g.Key.Code,
                Total = g.Sum(e => e.Amount - (e.Reimbursement == null ? 0m : e.Reimbursement.ExpectedAmount))
            })
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
                   + (topCategory is null ? string.Empty : $" Most of it went on {topCategory}.")
                   + await AgedReceivableSentenceAsync(userId, today, converter, cancellationToken);

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

    /// <summary>
    /// Stream R, sentence 4: a receivable is forward-looking, and one that has
    /// been out for more than a fortnight has stopped being forward-looking and
    /// started being forgotten. It rides the digest rather than earning a
    /// notification kind of its own — nobody wants a second buzz to be nagged.
    /// <para>
    /// Empty when nothing is that old, or when the open amounts cannot be
    /// converted: a sentence with a blank figure in it is worse than no
    /// sentence.
    /// </para>
    /// </summary>
    private async Task<string> AgedReceivableSentenceAsync(
        string userId, DateOnly today, CurrencyConverter converter, CancellationToken cancellationToken)
    {
        var cutoff = today.AddDays(-AgedReceivableDays);

        var receivables = await _context.Reimbursements
            .Where(r => r.UserId == userId && r.Expense.Date < cutoff)
            .Select(r => new
            {
                Code = r.Expense.Account.Currency.Code,
                r.ExpectedAmount,
                // Nullable on purpose: SUM over no settlement rows is NULL, and a
                // receivable nobody has paid a cent of is exactly the one this
                // sentence exists for.
                Settled = r.Settlements.Sum(s => (decimal?)s.Amount)
            })
            .ToListAsync(cancellationToken);

        var open = receivables
            .Select(r => new CurrencyAmount(r.Code, r.ExpectedAmount - (r.Settled ?? 0m)))
            .Where(a => a.Amount > 0m)
            .ToList();

        if (open.Count == 0) return string.Empty;

        var total = converter.Summarize(open);
        if (total.ConvertedTotal is not decimal owed || owed <= 0m) return string.Empty;

        return $" You're still owed {NotificationTargets.Money(owed, total.BaseCurrency)} from more than two weeks ago.";
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
