namespace ExpenseTracker.Application.DTOs
{
    public class SavingDTO
    {
        public Guid? Id { get; set; }
        public decimal Amount { get; set; }
        public DateTime Date { get; set; }
        public Guid AccountId { get; set; }
        public Guid? CategoryId { get; set; }
        public string? Description { get; set; }
    }
}
