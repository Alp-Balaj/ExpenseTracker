namespace ExpenseTracker.Application.DTOs
{
    public class IncomeDTO
    {
        public Guid Id { get; set; }
        public string Source { get; set; }
        public decimal Amount { get; set; }
        public DateTime Date { get; set; }
        public string Category { get; set; }
        public string? Description { get; set; }
        public string? PaymentMethod { get; set; }
    }
}
