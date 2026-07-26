using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Entities.User;
using ExpenseTracker.Domain.Interfaces;
using ExpenseTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Infrastructure.Repositories
{
    // Mutations only stage changes; callers commit with SaveAsync so multi-step
    // operations (revoke old + add new during rotation) persist atomically.
    public class RefreshTokenRepository : IRefreshTokenRepository
    {
        private readonly ApplicationDbContext _context;

        public RefreshTokenRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public void Add(RefreshToken token)
        {
            _context.RefreshTokens.Add(token);
        }

        public Task<RefreshToken?> GetByHashAsync(string tokenHash)
        {
            return _context.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == tokenHash);
        }

        public void Revoke(RefreshToken token, string? replacedByTokenHash = null)
        {
            token.RevokedAtUtc = DateTime.UtcNow;
            token.ReplacedByTokenHash = replacedByTokenHash;
        }

        public async Task RevokeAllActiveForUserAsync(string userId)
        {
            var now = DateTime.UtcNow;

            var active = await _context.RefreshTokens
                .Where(x => x.UserId == userId && x.RevokedAtUtc == null && x.ExpiresAtUtc > now)
                .ToListAsync();

            foreach (var t in active)
                t.RevokedAtUtc = now;
        }

        public Task SaveAsync() => _context.SaveChangesAsync();
    }
}
