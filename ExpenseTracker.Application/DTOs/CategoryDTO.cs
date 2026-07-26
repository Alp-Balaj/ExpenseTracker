using ExpenseTracker.Application.Validation;
using ExpenseTracker.Shared.Enums;
using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Application.DTOs
{
    public class CategoryDTO
    {
        public Guid? Id { get; set; }

        [Required, NotWhitespace, MaxLength(50)]
        public required string Name { get; set; }

        [MaxLength(200)]
        public string? Description { get; set; }

        [EnumDataType(typeof(CategoryType))]
        public int CategoryType { get; set; }

        [MaxLength(20)]
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
