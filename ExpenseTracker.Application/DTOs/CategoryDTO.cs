namespace ExpenseTracker.Application.DTOs
{
    public class CategoryDTO
    {
        public Guid? Id { get; set; }
        public required string Name { get; set; }
        public string? Description { get; set; }
        public int CategoryType { get; set; }
        public string? Color { get; set; }
    }

    public class CategorySummaryDTO 
    {
        public Guid? Id { get; set; }
        public required string Name { get; set; }
        public string? Description { get; set; }
        public int CategoryType { get; set; }
        public decimal TotalAmount { get; set; }
        public string? Color { get; set; }

    }
}