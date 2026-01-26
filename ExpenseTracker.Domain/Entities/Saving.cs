using ExpenseTracker.Domain.Entities.Common;

namespace ExpenseTracker.Domain.Entities
{
    public class Saving : BaseEntity
    {
        public decimal Amount { get; set; }
        public DateTime Date { get; set; }
        public string? Description { get; set; }

        public Guid CategoryId { get; set; }
        public Guid AccountId { get; set; }
        public Category Category { get; set; }
        public Account Account { get; set; }
    }
}
