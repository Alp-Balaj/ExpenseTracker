using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces;
using ExpenseTracker.Infrastructure.Persistence;
using ExpenseTracker.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore.Storage;

namespace ExpenseTracker.Infrastructure
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly ApplicationDbContext _context;
        private readonly ICurrentUserServices _currentUserServices;
        public IUserRelatedRepository<Account> Accounts { get; }
        public IUserRelatedRepository<Budget> Budgets { get; }
        public IUserRelatedRepository<Category> Categories { get; }
        public IUserRelatedRepository<Currency> Currencies { get; }
        public IUserRelatedRepository<Expense> Expenses { get; }
        public IUserRelatedRepository<FutureExpense> FutureExpenses { get; }
        public IUserRelatedRepository<Income> Incomes { get; }
        public IUserRelatedRepository<SavingGoal> SavingGoals { get; }
        public IUserRelatedRepository<SavingContribution> SavingContributions { get; }
        public IAccountRepository AccountRepository { get; }
        public UnitOfWork(ApplicationDbContext context, ICurrentUserServices currentUserServices)
        {
            _context = context;
            _currentUserServices = currentUserServices;

            Accounts = new UserRelatedRepository<Account>(_context, _currentUserServices);
            Budgets = new UserRelatedRepository<Budget>(_context, _currentUserServices);
            Categories = new UserRelatedRepository<Category>(_context, _currentUserServices);
            Currencies = new UserRelatedRepository<Currency>(_context, _currentUserServices);
            Expenses = new UserRelatedRepository<Expense>(_context, _currentUserServices);
            FutureExpenses = new UserRelatedRepository<FutureExpense>(_context, _currentUserServices);
            Incomes = new UserRelatedRepository<Income>(_context, _currentUserServices);
            SavingGoals = new UserRelatedRepository<SavingGoal>(_context, _currentUserServices);
            SavingContributions = new UserRelatedRepository<SavingContribution>(_context, _currentUserServices);
            AccountRepository = new AccountRepository(_context, _currentUserServices);
        }

        public async Task<int> SaveAsync() => await _context.SaveChangesAsync();

        public async Task<IUnitOfWorkTransaction> BeginTransactionAsync() =>
            new EfUnitOfWorkTransaction(await _context.Database.BeginTransactionAsync());

        public void Dispose() => _context.Dispose();

        private sealed class EfUnitOfWorkTransaction : IUnitOfWorkTransaction
        {
            private readonly IDbContextTransaction _transaction;
            public EfUnitOfWorkTransaction(IDbContextTransaction transaction) => _transaction = transaction;
            public Task CommitAsync() => _transaction.CommitAsync();
            public Task RollbackAsync() => _transaction.RollbackAsync();
            public ValueTask DisposeAsync() => _transaction.DisposeAsync();
        }
    }
}
