using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces;
using ExpenseTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Infrastructure.Repositories
{
    public class AccountRepository : IAccountRepository
    {
        private readonly ApplicationDbContext _context;
        private readonly ICurrentUserServices _user;
        private readonly DbSet<Account> _dbSet;

        public AccountRepository(ApplicationDbContext context, ICurrentUserServices user)
        {
            _context = context;
            _user = user;
            _dbSet = _context.Set<Account>();
        }

        private string _UserId => _user.UserId;

        public async Task<(IReadOnlyList<Account> Items, int TotalCount)> GetUserDataPageWithCurrencyAsync(
            int? skip,
            int? take,
            CancellationToken cancellationToken = default)
        {
            var rows = _dbSet
                .AsNoTracking()
                .Where(e => e.UserId == _UserId);

            var ordered = rows
                .Include(e => e.Currency)
                .OrderByDescending(e => e.CreatedAt)
                .ThenBy(e => e.Id);

            if (skip is null && take is null)
            {
                var all = await ordered.ToListAsync(cancellationToken);
                return (all, all.Count);
            }

            var total = await rows.CountAsync(cancellationToken);

            IQueryable<Account> window = ordered;

            if (skip is int skipCount and > 0)
                window = window.Skip(skipCount);

            if (take is int takeCount)
                window = window.Take(takeCount);

            return (await window.ToListAsync(cancellationToken), total);
        }

        public async Task<Account?> GetUserDataWithCurrencyByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await _dbSet
                .Where(e => e.UserId == _UserId)
                .Include(e => e.Currency)
                .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        }

        public async Task<bool> AdjustBalanceAsync(Guid accountId, decimal delta, CancellationToken cancellationToken = default)
        {
            var affected = await _dbSet
                .Where(a => a.Id == accountId && a.UserId == _UserId)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Balance, a => a.Balance + delta), cancellationToken);

            return affected > 0;
        }
    }
}
