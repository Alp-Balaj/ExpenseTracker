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
/// Trigger 2 of decision D3: a planned bill comes due tomorrow.
/// <para>
/// Tomorrow, not today, because the notification only earns its place if there is
/// still time to do something about it — moving money, or remembering that the
/// card on file expired.
/// </para>
/// <para>
/// Computed from the templates rather than from the queue. Occurrences are only
/// ever materialised up to today (Sugg-4: nothing is queued before it is actually
/// owed), so tomorrow's rent has no row yet by construction —
/// <see cref="PlannedSchedule.NextDueAfter"/> is what knows about it.
/// </para>
/// <para>
/// One notification a day covering everything due, not one per bill: three
/// separate buzzes for three subscriptions renewing on the 1st is how people
/// learn to swipe these away without reading them.
/// </para>
/// </summary>
public sealed class PlannedDueReminderJob : IScheduledJob
{
    private readonly ApplicationDbContext _context;
    private readonly IPushNotificationService _notifications;
    private readonly IPushSender _sender;
    private readonly ILogger<PlannedDueReminderJob> _logger;

    public PlannedDueReminderJob(
        ApplicationDbContext context,
        IPushNotificationService notifications,
        IPushSender sender,
        ILogger<PlannedDueReminderJob> logger)
    {
        _context = context;
        _notifications = notifications;
        _sender = sender;
        _logger = logger;
    }

    public string Name => ScheduledJobNames.PlannedDueReminders;

    public TimeSpan Interval => TimeSpan.FromHours(24);

    public bool Enabled => _sender.IsConfigured;

    public async Task<string> RunAsync(ScheduledJobContext context, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(context.NowUtc);
        var tomorrow = today.AddDays(1);

        var userIds = await NotificationTargets.WithSubscriptionsAsync(_context, cancellationToken);
        var sent = 0;

        foreach (var userId in userIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                sent += await RemindUserAsync(userId, today, tomorrow, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Planned due reminder failed for user {UserId}; continuing.", userId);
            }
        }

        return $"{sent} reminder(s) sent for {tomorrow:yyyy-MM-dd}.";
    }

    private async Task<int> RemindUserAsync(
        string userId, DateOnly today, DateOnly tomorrow, CancellationToken cancellationToken)
    {
        var templates = await _context.PlannedTransactions
            .Where(p => p.UserId == userId && p.IsActive)
            .Include(p => p.FromAccount)!.ThenInclude(a => a!.Currency)
            .ToListAsync(cancellationToken);

        if (templates.Count == 0) return 0;

        var dueTomorrow = templates
            .Where(t => PlannedSchedule.NextDueAfter(t, today) == tomorrow)
            .ToList();

        if (dueTomorrow.Count == 0) return 0;

        var converter = await NotificationTargets.ConverterForAsync(_context, userId, cancellationToken);

        // A template's amount carries no currency of its own; it is denominated in
        // whatever the account it is paid from is. Without an account there is
        // nothing to convert from, so it is assumed to be base — the same
        // assumption CurrencyConverter makes for a null currency.
        var amounts = dueTomorrow
            .Select(t => new CurrencyAmount(t.FromAccount?.Currency?.Code ?? string.Empty, t.Amount))
            .ToList();

        var breakdown = converter.Summarize(amounts);

        var titles = dueTomorrow.Select(t => t.Title).ToList();
        var names = titles.Count <= 3
            ? string.Join(", ", titles)
            : $"{string.Join(", ", titles.Take(2))} and {titles.Count - 2} more";

        var total = breakdown.ConvertedTotal is decimal converted
            ? $" — about {NotificationTargets.Money(converted, breakdown.BaseCurrency)}"
            : string.Empty;

        var delivered = await _notifications.SendToUserAsync(
            userId,
            NotificationKind.PlannedDue,
            // The date alone: one reminder per day about that day, however the
            // set of bills behind it changes during the day.
            tomorrow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            new PushMessage(
                dueTomorrow.Count == 1 ? "Due tomorrow" : $"{dueTomorrow.Count} due tomorrow",
                $"{names}{total}.",
                "/planned",
                "planned-due"),
            cancellationToken);

        return delivered > 0 ? 1 : 0;
    }
}
