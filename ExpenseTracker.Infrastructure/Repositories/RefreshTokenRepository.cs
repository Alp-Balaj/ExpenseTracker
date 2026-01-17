using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Entities.User;
using ExpenseTracker.Domain.Interfaces;
using ExpenseTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Infrastructure.Repositories
{
    public class RefreshTokenRepository : IRefreshTokenRepository
    {
        private readonly ApplicationDbContext _context;

        public RefreshTokenRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(RefreshToken token)
        {
            _context.RefreshTokens.Add(token);
            await _context.SaveChangesAsync();
        }

        public Task<RefreshToken?> GetByHashAsync(string tokenHash)
        {
            return _context.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == tokenHash);
        }

        public async Task RevokeAsync(RefreshToken token, string? replacedByTokenHash = null)
        {
            token.RevokedAtUtc = DateTime.UtcNow;
            token.ReplacedByTokenHash = replacedByTokenHash;
            await _context.SaveChangesAsync();
        }

        public async Task RevokeAllActiveForUserAsync(string userId)
        {
            var now = DateTime.UtcNow;

            var active = await _context.RefreshTokens
                .Where(x => x.UserId == userId && x.RevokedAtUtc == null && x.ExpiresAtUtc > now)
                .ToListAsync();

            foreach (var t in active)
                t.RevokedAtUtc = now;

            await _context.SaveChangesAsync();
        }
    }
}
