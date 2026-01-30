using System.Diagnostics.CodeAnalysis;
using ExpenseTracker.Domain.Entities.Common;
using ExpenseTracker.Shared.Enums;

namespace ExpenseTracker.Domain.Entities
{
    public class Account : BaseEntity
    {
        [SetsRequiredMembers]
        public Account() : base()
        {
            UserId = null!;
            Name = null!;
            AmountType = default!;
            Balance = default!;
            CurrencyId = default!;
            Currency = null!;
        }
        public required string Name { get; set; }
        public required AmountType AmountType { get; set; }
        public required decimal Balance { get; set; }
        public string? Description { get; set; }

        public required Guid CurrencyId { get; set; }
        public required Currency Currency { get; set; }

        public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
        public ICollection<Income> Incomes { get; set; } = new List<Income>();
        public ICollection<Saving> Savings { get; set; } = new List<Saving>();
    }
}