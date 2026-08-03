using ExpenseTracker.Domain.Entities;

namespace ExpenseTracker.Domain.Interfaces
{
    public interface IUnitOfWork : IDisposable
    {
        IUserRelatedRepository<Account> Accounts { get; }
        IUserRelatedRepository<Budget> Budgets { get; }
        IUserRelatedRepository<Category> Categories { get; }
        IUserRelatedRepository<Currency> Currencies { get; }
        IUserRelatedRepository<Expense> Expenses { get; }
        IUserRelatedRepository<Income> Incomes { get; }
        IUserRelatedRepository<SavingGoal> SavingGoals { get; }
        IUserRelatedRepository<SavingContribution> SavingContributions { get; }
        IUserRelatedRepository<PlannedTransaction> PlannedTransactions { get; }
        IUserRelatedRepository<PlannedOccurrence> PlannedOccurrences { get; }
        IUserRelatedRepository<Reimbursement> Reimbursements { get; }
        IUserRelatedRepository<ReimbursementSettlement> ReimbursementSettlements { get; }
        IUserRelatedRepository<Transfer> Transfers { get; }
        IAccountRepository AccountRepository { get; }
        Task<int> SaveAsync(CancellationToken cancellationToken = default);
        Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
    }
}