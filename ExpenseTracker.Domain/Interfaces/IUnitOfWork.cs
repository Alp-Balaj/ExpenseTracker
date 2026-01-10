using ExpenseTracker.Domain.Entities;

namespace ExpenseTracker.Domain.Interfaces
{
    public interface IUnitOfWork : IDisposable
    {
        IUserRelatedRepository<Account> Accounts { get; }
        IUserRelatedRepository<Category> Categories { get; }
        IUserRelatedRepository<Currency> Currencies { get; }
        IUserRelatedRepository<Expense> Expenses { get; }
        IUserRelatedRepository<Income> Incomes { get; }
        IUserRelatedRepository<Saving> Savings { get; }
        IUserRelatedRepository<FutureExpense> FutureExpenses { get; }
        Task<int> SaveAsync();
    }
}