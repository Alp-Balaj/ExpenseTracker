using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Infrastructure.Services;

/// <inheritdoc cref="IPushSubscriptionService"/>
public sealed class PushSubscriptionService : IPushSubscriptionService
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserServices _currentUser;

    public PushSubscriptionService(ApplicationDbContext context, ICurrentUserServices currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<PushDeviceDTO> SubscribeAsync(
        SavePushSubscriptionDTO subscription, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId;
        var endpoint = subscription.Endpoint.Trim();

        // Looked up by endpoint alone, not (user, endpoint): the browser mints one
        // endpoint per installation, so if this device was last registered by a
        // different account the row has to move rather than be duplicated —
        // otherwise the previous account keeps receiving pushes on a phone that is
        // now signed in as someone else.
        var existing = await _context.PushSubscriptions
            .FirstOrDefaultAsync(s => s.Endpoint == endpoint, cancellationToken);

        if (existing is null)
        {
            existing = new PushSubscription
            {
                UserId = userId,
                Endpoint = endpoint,
                P256dh = subscription.Keys.P256dh.Trim(),
                Auth = subscription.Keys.Auth.Trim()
            };
            _context.PushSubscriptions.Add(existing);
        }
        else
        {
            existing.UserId = userId;
            // The browser may re-key an endpoint it is keeping; take whatever it
            // just handed us as the truth.
            existing.P256dh = subscription.Keys.P256dh.Trim();
            existing.Auth = subscription.Keys.Auth.Trim();
        }

        existing.UserAgent = Truncate(subscription.UserAgent, 200);
        existing.FailureCount = 0;
        existing.LastFailureAtUtc = null;

        await _context.SaveChangesAsync(cancellationToken);

        return ToDto(existing, isCurrentDevice: true);
    }

    public async Task<bool> UnsubscribeAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId;
        var trimmed = endpoint.Trim();

        var subscription = await _context.PushSubscriptions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Endpoint == trimmed, cancellationToken);

        // Scoped to the caller: an endpoint is guessable in the sense that it is a
        // URL, and unsubscribing someone else's device is not something this
        // endpoint is going to offer.
        if (subscription is null) return false;

        _context.PushSubscriptions.Remove(subscription);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<PushDeviceDTO>> ListAsync(
        string? currentEndpoint = null, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId;
        var trimmed = currentEndpoint?.Trim();

        var rows = await _context.PushSubscriptions
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);

        return rows
            .Select(s => ToDto(s, isCurrentDevice: trimmed is not null && s.Endpoint == trimmed))
            .ToList();
    }

    private static PushDeviceDTO ToDto(PushSubscription subscription, bool isCurrentDevice) => new()
    {
        Id = subscription.Id,
        UserAgent = subscription.UserAgent,
        CreatedAt = subscription.CreatedAt,
        LastSuccessAtUtc = subscription.LastSuccessAtUtc,
        IsCurrentDevice = isCurrentDevice
    };

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= maxLength ? value : value[..maxLength];
}
