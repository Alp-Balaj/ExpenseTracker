using System.Diagnostics.CodeAnalysis;
using ExpenseTracker.Domain.Entities.Common;

namespace ExpenseTracker.Domain.Entities
{
    public class FutureExpense : BaseEntity
    {
        [SetsRequiredMembers]
        public FutureExpense() : base()
        {
            UserId = null!;
            Title = null!;
            Amount = default!;
            PredictedDate = default!;

            CategoryId = default!;
            Category = null!;
        }
        public required string Title { get; set; }
        public required decimal Amount { get; set; }
        public required DateTime? PredictedDate { get; set; }
        public string? Description { get; set; }

        public required Guid CategoryId { get; set; }
        public required Category Category { get; set; }
    }
}
