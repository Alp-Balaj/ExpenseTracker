namespace ExpenseTracker.Application.DTOs
{
    /// <summary>One instalment that has actually come back, and where it landed.</summary>
    public class ReimbursementSettlementDTO
    {
        public Guid Id { get; set; }
        public Guid AccountId { get; set; }
        public string AccountName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateOnly Date { get; set; }
    }
}
