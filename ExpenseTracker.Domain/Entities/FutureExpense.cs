using ExpenseTracker.Domain.Entities.Common;

namespace ExpenseTracker.Domain.Entities
{
    public class FutureExpense : BaseEntity
    {
        public string Title { get; set; }
        public decimal Amount { get; set; }
        public DateTime? PredictedDate { get; set; }
        public string Category { get; set; }
        public string? Description { get; set; }
    }
}
