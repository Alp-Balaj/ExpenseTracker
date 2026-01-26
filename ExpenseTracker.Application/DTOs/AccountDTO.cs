using ExpenseTracker.Shared.Enums;

namespace ExpenseTracker.Application.DTOs
{
    public class AccountDTO
    {
        public Guid? Id { get; set; }
        public string Name { get; set; }
        public int AmountType { get; set; }
        public decimal Balance { get; set; }
        public Guid CurrencyId { get; set; }
        public string? Description { get; set; }
    }
}