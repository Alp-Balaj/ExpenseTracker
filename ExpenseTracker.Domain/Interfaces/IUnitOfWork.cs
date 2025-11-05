using ExpenseTracker.Domain.Entities;

namespace ExpenseTracker.Domain.Interfaces
{
    public interface IUnitOfWork : IDisposable
    {
        IRepository<Expense> Expenses { get; }
        IRepository<Income> Incomes { get; }
        IRepository<Saving> Savings { get; }
        IRepository<FutureExpense> FutureExpenses { get; }
        Task<int> SaveAsync();
    }
}
