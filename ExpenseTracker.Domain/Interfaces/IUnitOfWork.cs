using ExpenseTracker.Domain.Entities;

namespace ExpenseTracker.Domain.Interfaces
{
    public interface IUnitOfWork : IDisposable
    {
        IUserRelatedRepository<Expense> Expenses { get; }
        IUserRelatedRepository<Income> Incomes { get; }
        IUserRelatedRepository<Saving> Savings { get; }
        IUserRelatedRepository<FutureExpense> FutureExpenses { get; }
        Task<int> SaveAsync();
    }
}
