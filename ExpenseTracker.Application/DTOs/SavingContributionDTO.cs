namespace ExpenseTracker.Application.DTOs
{
    public class SavingContributionDTO
    {
        public Guid? Id { get; set; }
        public Guid GoalId { get; set; }
        public decimal Amount { get; set; }
        public DateTime Date { get; set; }
        public string? Note { get; set; }

    }
}
