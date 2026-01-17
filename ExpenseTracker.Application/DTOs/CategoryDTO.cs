namespace ExpenseTracker.Application.DTOs
{
    public class CategoryDTO
    {
        public Guid? Id { get; set; }
        public string Name { get; set; }
        public string? Description { get; set; }
        public int CategoryTypeId { get; set; }
        public string? Color { get; set; }
    }
}