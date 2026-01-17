namespace ExpenseTracker.Application.DTOs
{
    public class ExpenseDTO
    {
        public Guid? Id { get; set; }
        public string Title { get; set; }
        public decimal Amount { get; set; }
        public DateTime Date { get; set; }
        public string? Description { get; set; }

        public Guid AccountId { get; set; }
        public Guid? CategoryId { get; set; }
    }
}