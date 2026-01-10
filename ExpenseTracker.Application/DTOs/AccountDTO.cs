using ExpenseTracker.Shared.Enums;

namespace ExpenseTracker.Application.DTOs
{
    public class AccountDTO
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public int AmountTypeId { get; set; }
        public Guid BalanceCurrencyId { get; set; }
    }
}