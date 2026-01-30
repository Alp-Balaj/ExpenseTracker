namespace ExpenseTracker.Application.DTOs
{
    public class FutureExpenseDTO
    {
        public Guid? Id { get; set; }
        public required string Title { get; set; }
        public decimal Amount { get; set; }
        public DateTime? Date { get; set; }
        public Guid CategoryId { get; set; }
        public string? Description { get; set; }
    }
}
