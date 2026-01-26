using ExpenseTracker.Domain.Entities.Common;
using ExpenseTracker.Shared.Enums;

namespace ExpenseTracker.Domain.Entities
{
    public class Account : BaseEntity
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public AmountType AmountType { get; set; }
        public decimal Balance { get; set; }
        public string? Description { get; set; }

        public Guid CurrencyId { get; set; }
        public Currency Currency { get; set; }

        public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
        public ICollection<Income> Incomes { get; set; } = new List<Income>();
        public ICollection<Saving> Savings { get; set; } = new List<Saving>();
    }
}