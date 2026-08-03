using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Infrastructure.Services;

/// <inheritdoc cref="IIdempotencyStore"/>
public sealed class IdempotencyStore : IIdempotencyStore
{
    /// <summary>
    /// How long an unfinished claim may sit before it is treated as abandoned.
    /// A process killed between the claim and the write would otherwise leave the
    /// client's key permanently in flight — retrying forever against a row that
    /// will never complete.
    /// </summary>
    private static readonly TimeSpan StaleClaim = TimeSpan.FromMinutes(5);

    private const int MaxResponseLength = 4000;

    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserServices _currentUser;

    public IdempotencyStore(ApplicationDbContext context, ICurrentUserServices currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<IdempotencyResult> TryClaimAsync(
        string key, string endpoint, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId;

        var existing = await _context.IdempotencyRecords
            .FirstOrDefaultAsync(r => r.UserId == userId && r.Key == key, cancellationToken);

        if (existing is null)
        {
            var record = new IdempotencyRecord
            {
                UserId = userId,
                Key = key,
                Endpoint = endpoint
            };

            _context.IdempotencyRecords.Add(record);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return new IdempotencyResult(IdempotencyClaim.Claimed);
            }
            catch (DbUpdateException)
            {
                // Two replays landed in the same instant and the unique index chose.
                // Detach so the loser's context is usable, then fall through and read
                // what the winner is doing.
                _context.Entry(record).State = EntityState.Detached;

                existing = await _context.IdempotencyRecords
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.UserId == userId && r.Key == key, cancellationToken);

                if (existing is null) return new IdempotencyResult(IdempotencyClaim.InFlight);
            }
        }

        if (!string.Equals(existing.Endpoint, endpoint, StringComparison.Ordinal))
            return new IdempotencyResult(IdempotencyClaim.EndpointMismatch);

        if (existing.StatusCode is int status)
            return new IdempotencyResult(IdempotencyClaim.AlreadyCompleted, status, existing.ResponseBody);

        // Claimed but never completed. Past the stale window the first attempt is
        // assumed dead and this caller takes the key over — otherwise a crash
        // between claim and write bricks that expense for ever.
        if (existing.CreatedAt <= DateTime.UtcNow - StaleClaim)
        {
            var taken = await _context.IdempotencyRecords
                .Where(r => r.UserId == userId && r.Key == key && r.StatusCode == null)
                .ExecuteUpdateAsync(
                    set => set.SetProperty(r => r.CreatedAt, DateTime.UtcNow),
                    cancellationToken);

            if (taken > 0) return new IdempotencyResult(IdempotencyClaim.Claimed);
        }

        return new IdempotencyResult(IdempotencyClaim.InFlight);
    }

    public async Task CompleteAsync(
        string key, int statusCode, string? responseBody, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId;

        var trimmed = responseBody is { Length: > MaxResponseLength }
            ? responseBody[..MaxResponseLength]
            : responseBody;

        await _context.IdempotencyRecords
            .Where(r => r.UserId == userId && r.Key == key)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(r => r.StatusCode, statusCode)
                    .SetProperty(r => r.ResponseBody, trimmed)
                    .SetProperty(r => r.CompletedAtUtc, DateTime.UtcNow),
                cancellationToken);
    }

    public async Task AbandonAsync(string key, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId;

        // Only an incomplete claim is abandonable — a completed one is the answer
        // and must survive.
        await _context.IdempotencyRecords
            .Where(r => r.UserId == userId && r.Key == key && r.StatusCode == null)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
