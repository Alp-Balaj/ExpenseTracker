using ExpenseTracker.Domain.Entities.Common;

namespace ExpenseTracker.Domain.Entities
{
    public class Expense : BaseEntity
    {
        public string Title { get; set; }
        public decimal Amount { get; set; }
        public DateTime Date { get; set; }
        public string? Description { get; set; }

        public Guid AccountId { get; set; }
        public Guid CategoryId { get; set; }

        public Account Account { get; set; }
        public Category Category { get; set; }
    }
}