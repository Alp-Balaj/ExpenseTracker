using ExpenseTracker.Domain.Entities;

namespace ExpenseTracker.Domain.Interfaces
{
    public interface IUnitOfWork : IDisposable
    {
        IRepository<Expense> Expenses { get; }
        IRepository<Income> Incomes { get; }
        Task<int> SaveAsync();
    }
}
