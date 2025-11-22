using ExpenseTracker.Domain.Entities.Common;
using Microsoft.Identity.Client;

namespace ExpenseTracker.Domain.Entities
{
    public class Income : BaseEntity
    {
        public decimal Amount { get; set; }
        public DateTime Date { get; set; }
        public string? Description { get; set; }

        public Guid AccountId { get; set; }
        public Guid? CategoryId { get; set; }
        
        public Account Account { get; set; }
        public Category? Source { get; set; }
    }
}
