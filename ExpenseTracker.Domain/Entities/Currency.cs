using System.Diagnostics.CodeAnalysis;
using ExpenseTracker.Domain.Entities.Common;

namespace ExpenseTracker.Domain.Entities
{
    public class Currency : BaseEntity
    {
        [SetsRequiredMembers]
        public Currency() : base()
        {
            UserId = null!;
            Name = null!;
            Code = null!;
            Symbol = null!;
            ExchangeRateToBase = default!;
        }
        public required string Name { get; set; }
        public required string Code { get; set; }             
        public required string Symbol { get; set; }
        public required decimal ExchangeRateToBase { get; set; }
    }
}