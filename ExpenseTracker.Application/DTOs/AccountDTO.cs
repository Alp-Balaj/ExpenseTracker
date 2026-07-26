using ExpenseTracker.Application.Validation;
using ExpenseTracker.Shared.Enums;
using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Application.DTOs
{
    public class AccountDTO
    {
        public Guid? Id { get; set; }

        [Required, NotWhitespace, MaxLength(100)]
        public required string Name { get; set; }

        [EnumDataType(typeof(AmountType))]
        public int AmountType { get; set; }

        public decimal Balance { get; set; }

        [NotDefault]
        public Guid CurrencyId { get; set; }

        public string? CurrencyCode { get; set; }

        [MaxLength(100)]
        public string? Description { get; set; }
    }
}
