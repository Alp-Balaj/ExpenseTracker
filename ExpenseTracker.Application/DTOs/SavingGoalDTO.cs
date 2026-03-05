namespace ExpenseTracker.Application.DTOs
{
    public class SavingGoalDTO
    {
        public Guid? Id { get; set; }
        public required string Title { get; set; }
        public decimal TargetAmount { get; set; }
        public decimal SavedAmount { get; set; }
        public DateTime Deadline { get; set; }
        public string? Description { get; set; }

        public Guid AccountId { get; set; }
        public Guid CategoryId { get; set; }
    }
}
