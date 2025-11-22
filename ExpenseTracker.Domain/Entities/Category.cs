using ExpenseTracker.Domain.Entities.Common;
using System.ComponentModel;
using ExpenseTracker.Shared.Enums;

namespace ExpenseTracker.Domain.Entities
{
    public class Category : BaseEntity
    {
        public string Name { get; set; }
        public string? Description { get; set; }
        public CategoryType CategoryType { get; set; }
        public string? Color { get; set; }


        public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
        public ICollection<Income> Incomes { get; set; } = new List<Income>();
        public ICollection<Saving> Savings { get; set; } = new List<Saving>();
        public ICollection<FutureExpense> FutureExpenses { get; set; } = new List<FutureExpense>();
    }
}
