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

        /// <summary>
        /// Server-derived, never accepted from a client: how much of this
        /// account's balance savings goals have spoken for. Usable money is
        /// <see cref="Balance"/> minus this, and it can exceed the balance —
        /// spending reserved money warns, it never blocks.
        /// </summary>
        public decimal ReservedAmount { get; set; }

        [NotDefault]
        public Guid CurrencyId { get; set; }

        public string? CurrencyCode { get; set; }

        [MaxLength(100)]
        public string? Description { get; set; }
    }
}
