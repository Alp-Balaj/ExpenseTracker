using System.Diagnostics.CodeAnalysis;
using ExpenseTracker.Domain.Entities.Common;
using ExpenseTracker.Shared.Enums;

namespace ExpenseTracker.Domain.Entities
{
    public class Category : BaseEntity
    {
        [SetsRequiredMembers]
        public Category() : base()
        {
            UserId = null!;
            Name = null!;
            CategoryType = default!;   
        }
        public required string Name { get; set; }
        public string? Description { get; set; }
        public required CategoryType CategoryType { get; set; }
        public string? Color { get; set; }

        public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
        public ICollection<Income> Incomes { get; set; } = new List<Income>();
        public ICollection<SavingGoal> SavingGoals { get; set; } = new List<SavingGoal>();
        public ICollection<PlannedTransaction> PlannedTransactions { get; set; } = new List<PlannedTransaction>();
        public ICollection<Budget> Budgets { get; set; } = new List<Budget>();
    }
}