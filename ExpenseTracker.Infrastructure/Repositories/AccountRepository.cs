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

        public async Task<IEnumerable<Account>> GetAllUserDataWithCurrencyAsync()
        {
            return await _dbSet
                .Where(e => e.UserId == _UserId)
                .Include(e => e.Currency)
                .ToListAsync();
        }
    }
}
