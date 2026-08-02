using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Infrastructure.Persistence;
using ExpenseTracker.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ExpenseTracker.Application.Options;

namespace ExpenseTracker.Infrastructure.Services;

/// <inheritdoc cref="IPushNotificationService"/>
/// <remarks>
/// The policy layer over <see cref="IPushSender"/>: who has already been told,
/// and which subscriptions are still worth keeping. In Infrastructure rather than
/// Application because both answers are database state, and the dedupe guarantee
/// is a unique index rather than a check in code.
/// </remarks>
public sealed class PushNotificationService : IPushNotificationService
{
    private readonly ApplicationDbContext _context;
    private readonly IPushSender _sender;
    private readonly PushOptions _options;
    private readonly ILogger<PushNotificationService> _logger;

    public PushNotificationService(
        ApplicationDbContext context,
        IPushSender sender,
        IOptions<PushOptions> options,
        ILogger<PushNotificationService> logger)
    {
        _context = context;
        _sender = sender;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> SendToUserAsync(
        string userId,
        NotificationKind kind,
        string dedupeKey,
        PushMessage message,
        CancellationToken cancellationToken = default)
    {
        if (!_sender.IsConfigured) return 0;

        var subscriptions = await LoadSubscriptionsAsync(userId, cancellationToken);

        // No devices means nothing to dedupe against either: recording a delivery
        // here would suppress the notification for a user who subscribes tomorrow
        // and is still, say, over budget.
        if (subscriptions.Count == 0) return 0;

        var delivery = new NotificationDelivery
        {
            UserId = userId,
            Kind = kind,
            DedupeKey = dedupeKey,
            SentAtUtc = DateTime.UtcNow
        };

        _context.NotificationDeliveries.Add(delivery);

        try
        {
            // Claim the event *before* sending. If the process dies between here
            // and the POST the user misses one budget warning; the other order
            // would send it again on every tick for the rest of the month, and
            // that is how a notification permission gets revoked.
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The unique (UserId, Kind, DedupeKey) index says this already went out.
            _context.Entry(delivery).State = EntityState.Detached;
            return 0;
        }

        var delivered = await SendToDevicesAsync(subscriptions, message, cancellationToken);

        delivery.DeviceCount = delivered;
        await _context.SaveChangesAsync(cancellationToken);

        return delivered;
    }

    public async Task<int> SendUndeduplicatedAsync(
        string userId, PushMessage message, CancellationToken cancellationToken = default)
    {
        if (!_sender.IsConfigured) return 0;

        var subscriptions = await LoadSubscriptionsAsync(userId, cancellationToken);
        if (subscriptions.Count == 0) return 0;

        var delivered = await SendToDevicesAsync(subscriptions, message, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return delivered;
    }

    private Task<List<PushSubscription>> LoadSubscriptionsAsync(string userId, CancellationToken cancellationToken) =>
        _context.PushSubscriptions
            .Where(s => s.UserId == userId)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Sends to every device and folds the outcome back into the rows. Does not
    /// save — the caller decides when, so a dedupe record and its device count
    /// land together.
    /// </summary>
    private async Task<int> SendToDevicesAsync(
        List<PushSubscription> subscriptions, PushMessage message, CancellationToken cancellationToken)
    {
        var delivered = 0;
        var now = DateTime.UtcNow;

        foreach (var subscription in subscriptions)
        {
            var result = await _sender.SendAsync(
                new PushTarget(subscription.Endpoint, subscription.P256dh, subscription.Auth),
                message,
                cancellationToken);

            switch (result.Outcome)
            {
                case PushSendOutcome.Delivered:
                    subscription.LastSuccessAtUtc = now;
                    subscription.FailureCount = 0;
                    delivered++;
                    break;

                case PushSendOutcome.Gone:
                    // Definitive. Keeping the row would mean pushing at a dead
                    // endpoint on every trigger for ever.
                    _context.PushSubscriptions.Remove(subscription);
                    _logger.LogInformation("Removed an expired push subscription for user {UserId}.", subscription.UserId);
                    break;

                case PushSendOutcome.TransientFailure:
                    subscription.LastFailureAtUtc = now;
                    subscription.FailureCount++;
                    if (subscription.FailureCount >= _options.MaxConsecutiveFailures)
                    {
                        // Never a definitive 404, but it has not accepted anything
                        // in five consecutive triggers — it is not coming back.
                        _context.PushSubscriptions.Remove(subscription);
                        _logger.LogInformation(
                            "Removed a push subscription for user {UserId} after {Failures} consecutive failures.",
                            subscription.UserId, subscription.FailureCount);
                    }
                    break;

                case PushSendOutcome.NotConfigured:
                default:
                    break;
            }
        }

        return delivered;
    }
}
