using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces;
using ExpenseTracker.Infrastructure.Persistence;
using ExpenseTracker.Infrastructure.Repositories;

namespace ExpenseTracker.Infrastructure
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly ApplicationDbContext _context;
        private readonly ICurrentUserServices _currentUserServices;
        public IUserRelatedRepository<Account> Accounts { get; }
        public IUserRelatedRepository<Category> Categories { get; }
        public IUserRelatedRepository<Currency> Currencies { get; }
        public IUserRelatedRepository<Expense> Expenses { get; }
        public IUserRelatedRepository<FutureExpense> FutureExpenses { get; }
        public IUserRelatedRepository<Income> Incomes { get; }
        public IUserRelatedRepository<Saving> Savings { get; }

        public UnitOfWork(ApplicationDbContext context, ICurrentUserServices currentUserServices)
        {
            _context = context;
            _currentUserServices = currentUserServices;

            Accounts = new UserRelatedRepository<Account>(_context, _currentUserServices);
            Categories = new UserRelatedRepository<Category>(_context, _currentUserServices);
            Currencies = new UserRelatedRepository<Currency>(_context, _currentUserServices);
            Expenses = new UserRelatedRepository<Expense>(_context, _currentUserServices);
            FutureExpenses = new UserRelatedRepository<FutureExpense>(_context, _currentUserServices);
            Incomes = new UserRelatedRepository<Income>(_context, _currentUserServices);
            Savings = new UserRelatedRepository<Saving>(_context, _currentUserServices);
        }

        public async Task<int> SaveAsync() => await _context.SaveChangesAsync();

        public void Dispose() => _context.Dispose();
    }
}
