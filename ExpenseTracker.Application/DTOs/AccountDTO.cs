namespace ExpenseTracker.Application.DTOs
{
    public class AccountDTO
    {
        public string Name { get; set; }
        public int TypeId { get; set; }
        public Guid BalanceCurrencyId { get; set; }
    }
}