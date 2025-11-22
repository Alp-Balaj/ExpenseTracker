using ExpenseTracker.Domain.Entities.Common;
using ExpenseTracker.Shared.Enums;

namespace ExpenseTracker.Domain.Entities
{
    public class Account : BaseEntity
    {
        public string Name { get; set; }
        public AmountType Type { get; set; }
        public decimal Balance { get; set; }

        public Guid BalanceCurrencyId { get; set; }
        public Currency BalanceCurrency { get; set; }

        public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
        public ICollection<Income> Incomes { get; set; } = new List<Income>();
        public ICollection<Saving> Savings { get; set; } = new List<Saving>();
    }
}
