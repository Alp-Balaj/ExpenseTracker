using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces;
using ExpenseTracker.Infrastructure.Persistence;
using ExpenseTracker.Infrastructure.Repositories;

namespace ExpenseTracker.Infrastructure
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly ApplicationDbContext _context;

        public IRepository<Expense> Expenses { get; }
        public IRepository<Income> Incomes { get; }
        public IRepository<Saving> Savings { get; }
        public IRepository<FutureExpense> FutureExpenses { get; }

        public UnitOfWork(ApplicationDbContext context)
        {
            _context = context;
            Expenses = new Repository<Expense>(_context);
            Incomes = new Repository<Income>(_context);
            Savings = new Repository<Saving>(_context);
            FutureExpenses = new Repository<FutureExpense>(_context);
        }

        public async Task<int> SaveAsync() => await _context.SaveChangesAsync();

        public void Dispose() => _context.Dispose();
    }
}
