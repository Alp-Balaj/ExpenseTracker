using System.Diagnostics.CodeAnalysis;
using ExpenseTracker.Domain.Entities.Common;

namespace ExpenseTracker.Domain.Entities
{
    public class Expense : BaseEntity
    {
        [SetsRequiredMembers]
        public Expense() : base()
        {
            UserId = null!;
            Title = null!;
            Amount = default!;
            Date = default!;
            
            AccountId = default!;
            CategoryId = default!;
            Account = null!;
            Category = null!;
        }
        public required string Title { get; set; }
        public required decimal Amount { get; set; }
        public required DateTime Date { get; set; }
        public string? Description { get; set; }

        public required Guid AccountId { get; set; }
        public required Guid CategoryId { get; set; }
        public required Account Account { get; set; }
        public required Category Category { get; set; }
    }
}