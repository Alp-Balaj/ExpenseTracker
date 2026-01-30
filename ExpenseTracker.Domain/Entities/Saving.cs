using System.Diagnostics.CodeAnalysis;
using ExpenseTracker.Domain.Entities.Common;

namespace ExpenseTracker.Domain.Entities
{
    public class Saving : BaseEntity
    {
        [SetsRequiredMembers]
        public Saving() : base()
        {
            UserId = null!;
            Amount = default!;
            Date = default!;

            CategoryId = default!;
            AccountId = default!;
            Category = null!;
            Account = null!;
        }
        public required decimal Amount { get; set; }
        public required DateTime Date { get; set; }
        public string? Description { get; set; }

        public required Guid CategoryId { get; set; }
        public required Guid AccountId { get; set; }
        public required Category Category { get; set; }
        public required Account Account { get; set; }
    }
}